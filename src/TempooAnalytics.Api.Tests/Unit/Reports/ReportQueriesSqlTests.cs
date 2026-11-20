using AwesomeAssertions;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Reports;

/// <summary>
/// What each report counts. The tracker also stores internal "leave" events (time on page, scroll depth): they are
/// neither page views nor custom events. ClickHouse is not in the test stack, so these check the SQL.
/// </summary>
public class ReportQueriesSqlTests
{
    [Fact]
    public void Pages_CountOnlyPageviews()
    {
        var sql = ReportQueries.LiveDimensionSql(Dimension.Path);

        sql.Should().Contain("SELECT path as Key, count() as Value");
        sql.Should().Contain("AND event_name = 'pageview'");
    }

    [Theory]
    [InlineData(Dimension.Device, "device_type")]
    [InlineData(Dimension.Browser, "browser")]
    [InlineData(Dimension.Os, "os")]
    public void DevicesBrowsersAndOs_CountUniqueVisitors(Dimension dimension, string column)
    {
        var sql = ReportQueries.LiveDimensionSql(dimension);

        sql.Should().Contain($"SELECT {column} as Key, uniqExact(visitor_id) as Value");
        sql.Should().Contain("AND event_name != 'leave'");
    }

    [Theory]
    [InlineData(Dimension.Referrer)]
    [InlineData(Dimension.Country)]
    [InlineData(Dimension.UtmSource)]
    [InlineData(Dimension.UtmMedium)]
    [InlineData(Dimension.UtmCampaign)]
    public void OtherTopLists_LeaveOutLeaveEvents(Dimension dimension)
    {
        var sql = ReportQueries.LiveDimensionSql(dimension);

        sql.Should().Contain("count() as Value");
        sql.Should().Contain("AND event_name != 'leave'");
    }

    [Fact]
    public void CustomEvents_LeaveOutPageviewsAndLeaveEvents()
    {
        EventNames.CustomEventCondition.Should().Be("event_name NOT IN ('pageview', 'leave')");
        ReportQueries.CustomEventsSql.Should().Contain("AND " + EventNames.CustomEventCondition + " AND");
    }

    // A session is a bounce when it has exactly one pageview and no custom event or goal. The automatic "leave" does
    // not count. Checked against ClickHouse by hand: pageview+leave => 1, 2 pageviews => 0, pageview+custom => 0,
    // leave+custom without pageview => 0, lone pageview => 1.
    [Fact]
    public void BouncedSession_IsOnePageviewAndNoInteractiveEvent_LeaveDoesNotCount()
    {
        EventNames.BouncedSessionCondition.Should().Be(
            "(countIf(event_name = 'pageview') = 1 AND countIf(event_name NOT IN ('pageview', 'leave')) = 0)");
        EventNames.BouncedSessionCondition.Should().Contain(EventNames.CustomEventCondition);
        EventNames.BouncedSessionCondition.Should().NotContain("count() =");
    }
}
