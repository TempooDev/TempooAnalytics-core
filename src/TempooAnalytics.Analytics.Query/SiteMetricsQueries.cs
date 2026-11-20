using System.Collections.Concurrent;
using System.Text;
using Dapper;

namespace TempooAnalytics.Analytics.Query;

public interface ISiteMetricsQueries
{
    /// <summary>
    /// Totals of the sites in [start, end): pageviews, unique visitors, sessions, bounce rate and average session
    /// duration. With <see cref="QueryOptions.IncludeImportedData"/> imported history before each site's first live
    /// day is added, unless the filter has conditions (then <c>ImportedDataExcludedByFilter</c> says so).
    /// </summary>
    Task<SiteMetricsSummaryDto> GetSummaryAsync(IReadOnlyCollection<Guid> siteIds, DateTime start, DateTime end, EventFilter? filter, QueryOptions options, CancellationToken ct = default);

    /// <summary>
    /// Sites with usable imported history in [start, end) and their cutoffs, or null. Cached per instance, so
    /// concurrent queries of one request (the dashboard) share one lookup.
    /// </summary>
    Task<ImportWindow?> GetImportWindowAsync(IEnumerable<Guid> siteIds, DateTime start, DateTime end);

    /// <summary>Imported totals of the whole window.</summary>
    Task<ImportedTotalsRow> GetImportedRangeTotalsAsync(ImportWindow window, CancellationToken ct = default);
}

/// <summary>
/// Register as scoped: the import window cache lives as long as the instance.
/// </summary>
public sealed class SiteMetricsQueries(IClickHouseConnectionFactory connections) : ISiteMetricsQueries
{
    // Imported rows are only used for days strictly before each site's first live event (the cutoff).
    public const string ImportedRowsFilter = @"
            WHERE i.site_id IN @importSiteIds
              AND i.date >= toDate(@start) AND toDateTime(i.date) < @end
              AND i.date < toDate(arrayElement(@importCutoffs, indexOf(@importSiteIds, i.site_id)))";

    private readonly ConcurrentDictionary<string, Lazy<Task<ImportWindow?>>> _importWindows = new();

    public async Task<SiteMetricsSummaryDto> GetSummaryAsync(IReadOnlyCollection<Guid> siteIds, DateTime start, DateTime end, EventFilter? filter, QueryOptions options, CancellationToken ct = default)
    {
        if (siteIds.Count == 0) return new SiteMetricsSummaryDto();
        using var conn = connections.Create();
        var sql = new StringBuilder($@"
            SELECT
                sum(pageviews) as Pageviews,
                uniqExact(visitor_id) as Visitors,
                uniqExact(session_id) as Sessions,
                sum(is_bounced) as BouncedSessions,
                sum(total_events) as TotalEvents,
                sum(custom_events) as CustomEvents,
                round(if(isFinite(avg(session_duration)), avg(session_duration), 0), 2) as AvgSessionDuration,
                round(if(isFinite(sum(is_bounced) / nullIf(Sessions, 0) * 100), sum(is_bounced) / nullIf(Sessions, 0) * 100, 0), 2) as BounceRate
            FROM (
                SELECT
                    session_id,
                    any(visitor_id) as visitor_id,
                    countIf(event_name = 'pageview') as pageviews,
                    count() as total_events,
                    countIf(event_name NOT IN ('pageview', 'leave')) as custom_events,
                    if({EventNames.BouncedSessionCondition}, 1, 0) as is_bounced,
                    if(count() > 1, least(toUInt32(max(timestamp) - min(timestamp)), 1800), 0) as session_duration

                FROM tempoo_analytics.events
                WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end
        ");
        var parameters = new DynamicParameters();
        parameters.Add("siteIds", siteIds.ToArray());
        parameters.Add("start", start);
        parameters.Add("end", end);
        filter?.AppendTo(sql, parameters);
        sql.Append(@"
                GROUP BY session_id
            ) 
        ");

        var liveTask = conn.QueryFirstTracedAsync<SiteMetricsSummaryDto>(sql.ToString(), parameters, ct);
        if (!options.IncludeImportedData) return await liveTask;

        // Also looked up with filters, to tell the user that imported history was left out.
        var window = await GetImportWindowAsync(siteIds, start, end);
        if (window == null) return await liveTask;

        var importedTask = GetImportedRangeTotalsAsync(window, ct);
        await Task.WhenAll(liveTask, importedTask);
        var live = liveTask.Result;
        var imported = importedTask.Result;
        if (imported.Visitors == 0 && imported.Pageviews == 0 && imported.Visits == 0) return live;

        if (filter is { HasConditions: true })
        {
            live.ImportedDataExcludedByFilter = true;
            return live;
        }
        return ImportedData.MergeSummary(live, imported);
    }

    public Task<ImportWindow?> GetImportWindowAsync(IEnumerable<Guid> siteIds, DateTime start, DateTime end)
    {
        var ids = siteIds.Order().ToArray();
        var key = $"{string.Join(',', ids)}|{start.Ticks}|{end.Ticks}";
        return _importWindows.GetOrAdd(key, _ => new Lazy<Task<ImportWindow?>>(() => LoadImportWindowAsync(ids, start, end))).Value;
    }

    private async Task<ImportWindow?> LoadImportWindowAsync(Guid[] siteIds, DateTime start, DateTime end)
    {
        using var conn = connections.Create();
        var imported = (await conn.QueryTracedAsync<ImportedSiteRange>(@"
            SELECT site_id AS SiteId, min(date) AS FirstImported, max(date) AS LastImported
            FROM tempoo_analytics.imported_daily_stats
            WHERE site_id IN @siteIds
            GROUP BY site_id", new { siteIds }, CancellationToken.None)).ToList();
        if (imported.Count == 0) return null;

        // Only sites with imported history pay for the cutoff scan.
        var firstLive = (await conn.QueryTracedAsync<(Guid SiteId, DateTime FirstLive)>(@"
            SELECT site_id AS SiteId, min(date) AS FirstLive
            FROM tempoo_analytics.events
            WHERE site_id IN @siteIds
            GROUP BY site_id", new { siteIds = imported.Select(x => x.SiteId).ToArray() }, CancellationToken.None))
            .ToDictionary(x => x.SiteId, x => x.FirstLive);

        return ImportedData.BuildWindow(imported, firstLive, start, end);
    }

    /// <summary>Parameters for <see cref="ImportedRowsFilter"/>.</summary>
    public static DynamicParameters WindowParameters(ImportWindow window)
    {
        var parameters = new DynamicParameters();
        parameters.Add("importSiteIds", window.SiteIds);
        // Sent as yyyy-MM-dd and parsed with toDate so the cutoff day doesn't depend on time zones.
        parameters.Add("importCutoffs", window.Cutoffs.Select(c => c.ToString("yyyy-MM-dd")).ToArray());
        parameters.Add("start", window.Start);
        parameters.Add("end", window.End);
        return parameters;
    }

    public async Task<ImportedTotalsRow> GetImportedRangeTotalsAsync(ImportWindow window, CancellationToken ct = default)
    {
        using var conn = connections.Create();
        var sql = $@"
            SELECT
                sum(i.visitors) AS Visitors,
                sum(i.pageviews) AS Pageviews,
                sum(i.visits) AS Visits,
                sum(i.bounces) AS Bounces,
                sum(i.visit_duration) AS VisitDuration
            FROM tempoo_analytics.imported_daily_stats AS i
            {ImportedRowsFilter}";
        return await conn.QueryFirstTracedAsync<ImportedTotalsRow>(sql, WindowParameters(window), ct);
    }
}
