namespace TempooAnalytics.Analytics.Query;

/// <summary>Imported totals (already limited to days before the cutoff) for one bucket or a whole range.</summary>
public sealed class ImportedTotalsRow
{
    public DateTime Date { get; set; }
    public long Visitors { get; set; }
    public long Pageviews { get; set; }
    public long Visits { get; set; }
    public long Bounces { get; set; }
    public long VisitDuration { get; set; }
}

/// <summary>Imported date range stored for one site (from imported_daily_stats).</summary>
public sealed class ImportedSiteRange
{
    public Guid SiteId { get; set; }
    public DateTime FirstImported { get; set; }
    public DateTime LastImported { get; set; }
}

/// <summary>
/// Sites whose imported history can contribute to a range, each with its cutoff (first live day).
/// Imported rows are used for days in the range that fall before the site's cutoff.
/// </summary>
public sealed record ImportWindow(Guid[] SiteIds, DateTime[] Cutoffs, DateTime Start, DateTime End)
{
    /// <summary>Cutoff for sites without live events, so every imported day counts.</summary>
    public static readonly DateTime NoCutoff = new(2100, 1, 1);
}

/// <summary>
/// Pure helpers to combine live Tempoo data with imported history. The cutoff (first live event per site) is
/// applied in ClickHouse, so imported and live rows never cover the same day; here they are only added up.
/// </summary>
public static class ImportedData
{
    /// <summary>
    /// Keeps the sites that have imported days inside [start, end) and before their first live day.
    /// Returns null when no site does, so the imported queries can be skipped.
    /// </summary>
    public static ImportWindow? BuildWindow(IEnumerable<ImportedSiteRange> imported, IReadOnlyDictionary<Guid, DateTime> firstLive, DateTime start, DateTime end)
    {
        var sites = new List<Guid>();
        var cutoffs = new List<DateTime>();
        foreach (var site in imported)
        {
            var cutoff = firstLive.TryGetValue(site.SiteId, out var first) ? first.Date : ImportWindow.NoCutoff;
            var firstUsable = site.FirstImported.Date > start.Date ? site.FirstImported.Date : start.Date;
            if (firstUsable <= site.LastImported.Date && firstUsable < cutoff && firstUsable < end)
            {
                sites.Add(site.SiteId);
                cutoffs.Add(cutoff);
            }
        }
        return sites.Count == 0 ? null : new ImportWindow([.. sites], [.. cutoffs], start, end);
    }

    public static SiteMetricsSummaryDto MergeSummary(SiteMetricsSummaryDto live, ImportedTotalsRow imported)
    {
        var sessions = live.Sessions + imported.Visits;
        var bounced = live.BouncedSessions + imported.Bounces;
        var durationSeconds = live.AvgSessionDuration * live.Sessions + imported.VisitDuration;

        return new SiteMetricsSummaryDto
        {
            Pageviews = live.Pageviews + imported.Pageviews,
            Visitors = live.Visitors + imported.Visitors,
            Sessions = sessions,
            BouncedSessions = bounced,
            // Imported history only has pageviews, which also count as events.
            TotalEvents = live.TotalEvents + imported.Pageviews,
            CustomEvents = live.CustomEvents,
            AvgSessionDuration = sessions > 0 ? Math.Round(durationSeconds / sessions, 2) : 0,
            BounceRate = sessions > 0 ? Math.Round((double)bounced / sessions * 100, 2) : 0,
            IncludesImportedData = true
        };
    }

    /// <summary>Adds imported values to live ones per key and returns the top <paramref name="top"/>.</summary>
    public static IDictionary<string, long> MergeDimension(IDictionary<string, long> live, IDictionary<string, long> imported, int top)
    {
        var merged = new Dictionary<string, long>(live);
        foreach (var (key, value) in imported)
            merged[key] = merged.GetValueOrDefault(key) + value;
        return merged
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(top)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
