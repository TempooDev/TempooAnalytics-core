using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;

namespace TempooAnalytics.Api.Features.Analytics.Repositories;

/// <summary>Live daily bucket with its session count, needed to weight the average duration.</summary>
public sealed class LiveDailyMetricRow
{
    public DateTime Date { get; set; }
    public long Visitors { get; set; }
    public long Pageviews { get; set; }
    public long Events { get; set; }
    public long Sessions { get; set; }
    public double AvgSessionDuration { get; set; }
}

/// <summary>
/// Combines live Tempoo data with imported history. The cutoff (first live event per site) is applied
/// in ClickHouse, so imported and live rows never cover the same day; here they are only added up.
/// Imported visitors can't be deduplicated across days, so they are summed per day.
/// </summary>
public static class ImportedDataMerger
{
    /// <summary>
    /// Imported history can't be filtered by other dimensions, so any filter excludes it.
    /// </summary>
    public static bool HasFilters(AnalyticsFilterDto? filter) =>
        filter != null && (
            !string.IsNullOrEmpty(filter.DeviceType) || !string.IsNullOrEmpty(filter.OS) ||
            !string.IsNullOrEmpty(filter.Browser) || !string.IsNullOrEmpty(filter.CountryCode) ||
            !string.IsNullOrEmpty(filter.Path) || !string.IsNullOrEmpty(filter.ReferrerDomain) ||
            !string.IsNullOrEmpty(filter.UtmSource) || !string.IsNullOrEmpty(filter.UtmMedium) ||
            !string.IsNullOrEmpty(filter.UtmCampaign) ||
            (filter.AdvancedFilters?.Any(f => f.Values.Count > 0) ?? false));

    /// <summary>
    /// Keeps the sites that have imported days inside [start, end) and before their first live day.
    /// Returns null when no site does, so the imported queries can be skipped.
    /// </summary>
    public static ImportWindow? BuildWindow(IEnumerable<ImportedSiteRange> imported, IReadOnlyDictionary<Guid, DateTime> firstLive, DateTime start, DateTime end) =>
        ImportedData.BuildWindow(imported, firstLive, start, end);

    public static IReadOnlyList<DailyMetricDto> MergeDaily(IEnumerable<LiveDailyMetricRow> live, IEnumerable<ImportedTotalsRow> imported)
    {
        var buckets = new SortedDictionary<DateTime, (long Visitors, long Pageviews, long Events, double DurationSeconds, long Sessions)>();

        foreach (var row in live)
        {
            buckets.TryGetValue(row.Date, out var b);
            buckets[row.Date] = (b.Visitors + row.Visitors, b.Pageviews + row.Pageviews, b.Events + row.Events,
                b.DurationSeconds + row.AvgSessionDuration * row.Sessions, b.Sessions + row.Sessions);
        }
        foreach (var row in imported)
        {
            buckets.TryGetValue(row.Date, out var b);
            buckets[row.Date] = (b.Visitors + row.Visitors, b.Pageviews + row.Pageviews, b.Events,
                b.DurationSeconds + row.VisitDuration, b.Sessions + row.Visits);
        }

        return buckets.Select(kv => new DailyMetricDto
        {
            Date = kv.Key,
            Visitors = kv.Value.Visitors,
            Pageviews = kv.Value.Pageviews,
            Events = kv.Value.Events,
            AvgSessionDuration = kv.Value.Sessions > 0 ? Math.Round(kv.Value.DurationSeconds / kv.Value.Sessions, 2) : 0
        }).ToList();
    }

    public static SiteMetricsSummaryDto MergeSummary(SiteMetricsSummaryDto live, ImportedTotalsRow imported) =>
        ImportedData.MergeSummary(live, imported);

    /// <summary>Adds imported values to live ones per key and returns the top <paramref name="top"/>.</summary>
    public static IDictionary<string, long> MergeDimension(IDictionary<string, long> live, IDictionary<string, long> imported, int top) =>
        ImportedData.MergeDimension(live, imported, top);
}
