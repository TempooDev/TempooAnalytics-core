using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Infrastructure.FeatureFlags;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Services;

public class ImportedDataMergerTests
{
    private static readonly DateTime Day1 = new(2026, 8, 30);
    private static readonly DateTime Day2 = new(2026, 8, 31);
    private static readonly DateTime Day3 = new(2026, 9, 1); // first live day (cutoff)

    [Fact]
    public void MergeDaily_ImportedDaysBeforeCutoff_AndLiveDaysAfter()
    {
        var live = new[] { new LiveDailyMetricRow { Date = Day3, Visitors = 40, Pageviews = 90, Events = 5, Sessions = 50, AvgSessionDuration = 60 } };
        var imported = new[]
        {
            new ImportedTotalsRow { Date = Day1, Visitors = 100, Pageviews = 200, Visits = 110, VisitDuration = 11000 },
            new ImportedTotalsRow { Date = Day2, Visitors = 150, Pageviews = 300, Visits = 160, VisitDuration = 8000 }
        };

        var result = ImportedDataMerger.MergeDaily(live, imported);

        result.Select(r => r.Date).Should().Equal(Day1, Day2, Day3);
        result[0].Visitors.Should().Be(100);
        result[0].AvgSessionDuration.Should().Be(100); // 11000 s / 110 visits
        result[1].Pageviews.Should().Be(300);
        result[2].Visitors.Should().Be(40);
        result[2].Events.Should().Be(5);
        result[2].AvgSessionDuration.Should().Be(60);
    }

    [Fact]
    public void MergeDaily_BucketWithImportedAndLiveDays_WeightsAverageDuration()
    {
        // A monthly bucket that spans the cutoff: 10 live sessions of 30 s and 30 imported visits totalling 2100 s.
        var month = new DateTime(2026, 9, 1);
        var live = new[] { new LiveDailyMetricRow { Date = month, Visitors = 8, Pageviews = 20, Sessions = 10, AvgSessionDuration = 30 } };
        var imported = new[] { new ImportedTotalsRow { Date = month, Visitors = 25, Pageviews = 60, Visits = 30, VisitDuration = 2100 } };

        var result = ImportedDataMerger.MergeDaily(live, imported).Single();

        result.Visitors.Should().Be(33);
        result.Pageviews.Should().Be(80);
        result.AvgSessionDuration.Should().Be(60); // (10*30 + 2100) / 40
    }

    [Fact]
    public void MergeSummary_SumsImportedVisitorsPerDay_AndCombinesRates()
    {
        // Two imported days with 100 and 150 visitors -> 250, plus the live unique visitors.
        var live = new SiteMetricsSummaryDto
        {
            Pageviews = 100, Visitors = 40, Sessions = 50, BouncedSessions = 25,
            TotalEvents = 120, CustomEvents = 20, AvgSessionDuration = 60, BounceRate = 50
        };
        var imported = new ImportedTotalsRow { Visitors = 250, Pageviews = 500, Visits = 150, Bounces = 35, VisitDuration = 15000 };

        var result = ImportedDataMerger.MergeSummary(live, imported);

        result.Visitors.Should().Be(290);
        result.Pageviews.Should().Be(600);
        result.Sessions.Should().Be(200);
        result.BouncedSessions.Should().Be(60);
        result.BounceRate.Should().Be(30); // (25 + 35) / (50 + 150)
        result.AvgSessionDuration.Should().Be(90); // (60*50 + 15000) / 200
        result.TotalEvents.Should().Be(620);
        result.CustomEvents.Should().Be(20);
        result.IncludesImportedData.Should().BeTrue();
    }

    [Fact]
    public void MergeSummary_SiteWithoutLiveEvents_UsesImportedOnly()
    {
        var imported = new ImportedTotalsRow { Visitors = 250, Pageviews = 500, Visits = 200, Bounces = 50, VisitDuration = 12000 };

        var result = ImportedDataMerger.MergeSummary(new SiteMetricsSummaryDto(), imported);

        result.Visitors.Should().Be(250);
        result.BounceRate.Should().Be(25);
        result.AvgSessionDuration.Should().Be(60);
    }

    [Fact]
    public void MergeDimension_TopPagesMixingPeriods_SumsImportedAndLiveViews()
    {
        var live = new Dictionary<string, long> { ["/"] = 50, ["/pricing"] = 30 };
        var imported = new Dictionary<string, long> { ["/"] = 200, ["/blog"] = 120 };

        var result = ImportedDataMerger.MergeDimension(live, imported, top: 20);

        result.Should().Equal(new Dictionary<string, long> { ["/"] = 250, ["/blog"] = 120, ["/pricing"] = 30 });
        result.Keys.Should().ContainInOrder("/", "/blog", "/pricing");
    }

    [Fact]
    public void MergeDimension_KeepsOnlyTheTopEntries()
    {
        var live = Enumerable.Range(1, 15).ToDictionary(i => $"/live-{i}", i => (long)i);
        var imported = Enumerable.Range(1, 15).ToDictionary(i => $"/old-{i}", i => (long)(i * 10));

        var result = ImportedDataMerger.MergeDimension(live, imported, top: 20);

        result.Should().HaveCount(20);
        result.First().Key.Should().Be("/old-15");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("mobile", true)]
    public void HasFilters_DetectsBasicFilters(string? device, bool expected)
    {
        var filter = device == null ? null : new AnalyticsFilterDto { DeviceType = device };
        ImportedDataMerger.HasFilters(filter).Should().Be(expected);
    }

    [Fact]
    public void HasFilters_DetectsAdvancedFilters()
    {
        var filter = new AnalyticsFilterDto
        {
            AdvancedFilters = { new AdvancedFilterDto { Property = "Country", Operator = "is", Values = { "ES" } } }
        };
        ImportedDataMerger.HasFilters(filter).Should().BeTrue();
        ImportedDataMerger.HasFilters(new AnalyticsFilterDto { AdvancedFilters = { new AdvancedFilterDto { Property = "Country" } } })
            .Should().BeFalse("a filter without values filters nothing");
    }

    private static readonly Guid SiteA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid SiteB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    // Imported Jan 1 - Jul 14; live events from Jul 15 (the cutoff).
    private static ImportedSiteRange ImportedA => new() { SiteId = SiteA, FirstImported = new(2026, 1, 1), LastImported = new(2026, 7, 14) };
    private static readonly Dictionary<Guid, DateTime> LiveFromJul15 = new() { [SiteA] = new(2026, 7, 15) };

    [Fact]
    public void BuildWindow_RangeAfterCutoff_IsNull()
    {
        ImportedDataMerger.BuildWindow([ImportedA], LiveFromJul15, new(2026, 9, 13), new(2026, 10, 13, 23, 59, 0))
            .Should().BeNull("every day in the range already has live data");
    }

    [Fact]
    public void BuildWindow_RangeBeforeImportedHistory_IsNull()
    {
        ImportedDataMerger.BuildWindow([ImportedA], LiveFromJul15, new(2025, 6, 1), new(2025, 7, 1)).Should().BeNull();
    }

    [Fact]
    public void BuildWindow_NoImportedSites_IsNull()
    {
        ImportedDataMerger.BuildWindow([], LiveFromJul15, new(2026, 1, 1), new(2026, 10, 13)).Should().BeNull();
    }

    [Fact]
    public void BuildWindow_RangeStraddlingCutoff_KeepsSiteWithItsCutoff()
    {
        var window = ImportedDataMerger.BuildWindow([ImportedA], LiveFromJul15, new(2026, 7, 10), new(2026, 7, 20, 23, 59, 0));

        window.Should().NotBeNull();
        window!.SiteIds.Should().Equal(SiteA);
        window.Cutoffs.Should().Equal(new DateTime(2026, 7, 15));
    }

    [Fact]
    public void BuildWindow_RangeStartingOnCutoffDay_IsNull()
    {
        ImportedDataMerger.BuildWindow([ImportedA], LiveFromJul15, new(2026, 7, 15, 10, 0, 0), new(2026, 7, 20)).Should().BeNull();
    }

    [Fact]
    public void BuildWindow_RangeEndingAtMidnightOfFirstImportedDay_IsNull()
    {
        // The end is exclusive, so a range ending at Jan 1 00:00 contains no imported day.
        ImportedDataMerger.BuildWindow([ImportedA], LiveFromJul15, new(2025, 12, 1), new(2026, 1, 1)).Should().BeNull();
    }

    [Fact]
    public void BuildWindow_SiteWithoutLiveEvents_UsesNoCutoff_AndSkipsSitesOutsideRange()
    {
        var importedB = new ImportedSiteRange { SiteId = SiteB, FirstImported = new(2026, 3, 1), LastImported = new(2026, 3, 31) };

        var window = ImportedDataMerger.BuildWindow([ImportedA, importedB], new Dictionary<Guid, DateTime>(), new(2026, 5, 1), new(2026, 6, 1));

        window!.SiteIds.Should().Equal(SiteA);
        window.Cutoffs.Should().Equal(ImportWindow.NoCutoff);
    }
}

public class FeatureFlagsTests
{
    private static ConfigurationFeatureFlags Flags(Dictionary<string, string?> values) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

    [Fact]
    public void PlausibleImport_IsOffByDefault()
    {
        Flags(new()).PlausibleImport.Should().BeFalse();
    }

    [Fact]
    public void PlausibleImport_CanBeEnabledFromConfiguration()
    {
        Flags(new() { ["FeatureFlags:PlausibleImport"] = "true" }).PlausibleImport.Should().BeTrue();
    }

    [Fact]
    public void Heatmaps_IsOffByDefault_AndCanBeEnabled()
    {
        Flags(new()).Heatmaps.Should().BeFalse();
        Flags(new() { ["FeatureFlags:Heatmaps"] = "true" }).Heatmaps.Should().BeTrue();
    }
}
