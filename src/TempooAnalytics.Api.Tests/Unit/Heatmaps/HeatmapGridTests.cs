using System;
using System.Linq;
using AwesomeAssertions;
using TempooAnalytics.Api.Features.Heatmaps;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Heatmaps;

public class HeatmapGridTests
{
    [Fact]
    public void ScrollDistribution_CountsAViewForEveryDepthItReached()
    {
        // 4 page views: 100 %, 50 %, 47 % and 5 %.
        var result = HeatmapGrid.ScrollDistribution([(100, 1), (50, 1), (47, 1), (5, 1)]);

        result.Select(r => r.Depth).Should().Equal(10, 20, 30, 40, 50, 60, 70, 80, 90, 100);
        result.Single(r => r.Depth == 10).ReachedPct.Should().Be(75);
        result.Single(r => r.Depth == 40).ReachedPct.Should().Be(75);
        result.Single(r => r.Depth == 50).ReachedPct.Should().Be(50);
        result.Single(r => r.Depth == 60).ReachedPct.Should().Be(25);
        result.Single(r => r.Depth == 100).ReachedPct.Should().Be(25);
    }

    [Fact]
    public void ScrollDistribution_WithoutData_IsAllZero()
    {
        HeatmapGrid.ScrollDistribution([]).Should().HaveCount(10).And.OnlyContain(r => r.ReachedPct == 0);
    }

    [Fact]
    public void Range_DefaultsToTheLast30DaysIncludingToday()
    {
        var (start, end) = HeatmapGrid.Range(null, null, new DateTime(2026, 10, 23, 15, 0, 0, DateTimeKind.Utc));

        end.Should().Be(new DateTime(2026, 10, 24));
        start.Should().Be(new DateTime(2026, 9, 24));
    }

    [Theory]
    [InlineData("mobile", 1, 767)]
    [InlineData("tablet", 768, 1023)]
    [InlineData("desktop", 1024, 65535)]
    public void WidthRange_MatchesTheIngestionThresholds(string device, int min, int max)
    {
        HeatmapDevices.WidthRange(device).Should().Be((min, max));
    }
}
