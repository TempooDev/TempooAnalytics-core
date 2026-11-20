using System;
using System.Linq;
using AwesomeAssertions;
using TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;
using TempooAnalytics.Api.Infrastructure.Common;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Services;

public class GapFillerTests
{
    private static readonly DateTime Today = new(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FillDailyMetricsGaps_OneDayWithoutBreakdown_KeepsTheHourlyBucketsTheQueryReturns()
    {
        // days=1 → [today, tomorrow): ClickHouse buckets by hour when no breakdown is given.
        var start = Today;
        var end = Today.AddDays(1);
        GapFiller.IsHourlyByDefault(start, end).Should().BeTrue();

        var metrics = new[]
        {
            new DailyMetricDto { Date = Today.AddHours(9), Pageviews = 2 },
            new DailyMetricDto { Date = Today.AddHours(14), Pageviews = 4 }
        };

        var result = GapFiller.FillDailyMetricsGaps(metrics, start, end, null).ToList();

        result.Should().HaveCount(24);
        result.Sum(m => m.Pageviews).Should().Be(6);
        result[9].Pageviews.Should().Be(2);
        result[14].Pageviews.Should().Be(4);
    }

    [Fact]
    public void FillDailyMetricsGaps_SevenDaysWithoutBreakdown_BucketsByDayIncludingToday()
    {
        var start = Today.AddDays(-6);
        var end = Today.AddDays(1);
        GapFiller.IsHourlyByDefault(start, end).Should().BeFalse();

        var metrics = new[] { new DailyMetricDto { Date = Today, Pageviews = 6 } };

        var result = GapFiller.FillDailyMetricsGaps(metrics, start, end, null).ToList();

        result.Should().HaveCount(7);
        result[^1].Date.Should().Be(Today);
        result[^1].Pageviews.Should().Be(6);
    }

    [Fact]
    public void FillDailyMetricsGaps_OneDayWithDayBreakdown_ReturnsTodayAsOneBucket()
    {
        var metrics = new[] { new DailyMetricDto { Date = Today, Pageviews = 6 } };

        var result = GapFiller.FillDailyMetricsGaps(metrics, Today, Today.AddDays(1), "day").ToList();

        result.Should().ContainSingle().Which.Pageviews.Should().Be(6);
    }
}
