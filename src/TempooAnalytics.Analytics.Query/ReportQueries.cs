using System.Text;
using Dapper;

namespace TempooAnalytics.Analytics.Query;

/// <summary>The sites, range and optional filter every report shares. The range is [Start, End).</summary>
public sealed record ReportScope(IReadOnlyCollection<Guid> SiteIds, DateTime Start, DateTime End, EventFilter? Filter, QueryOptions Options);

public enum SeriesInterval { Hour, Day, Week, Month }

public enum Dimension { Path, Referrer, Country, Device, Browser, Os, UtmSource, UtmMedium, UtmCampaign }

/// <summary>One bucket of a time series. Imported history adds visitors, pageviews, sessions, bounces and duration.</summary>
public sealed record SeriesPoint(DateTime Date, long Visitors, long Pageviews, long Events, long Sessions, long Bounces, double AvgSessionDuration);

/// <summary>A value of a dimension (page, referrer, country...) and its count, ordered by count descending.</summary>
public sealed record DimensionRow(string Value, long Views);

public sealed record CustomEventRow(string Name, long Count, long Visitors);

/// <summary>
/// The traffic reports of the dashboard and the public API: time series, top lists by dimension, channels, entry and
/// exit pages and custom events. One SQL per report, so both read exactly the same numbers. Register as scoped
/// (it shares the import-window lookup with <see cref="ISiteMetricsQueries"/>).
/// </summary>
public interface IReportQueries
{
    Task<IReadOnlyList<SeriesPoint>> GetSeriesAsync(ReportScope scope, SeriesInterval interval, CancellationToken ct = default);

    /// <summary>
    /// Top <paramref name="limit"/> values of a dimension. With imported data both sides fetch more candidates so the
    /// merged list is accurate; dimensions imported history has no rows for (UTM) are live only.
    /// </summary>
    Task<IReadOnlyList<DimensionRow>> GetDimensionAsync(ReportScope scope, Dimension dimension, int limit, CancellationToken ct = default);

    Task<IReadOnlyList<DimensionRow>> GetChannelsAsync(ReportScope scope, CancellationToken ct = default);

    /// <summary>Landing pages (first pageview of each session).</summary>
    Task<IReadOnlyList<DimensionRow>> GetEntryPagesAsync(ReportScope scope, int limit, CancellationToken ct = default);

    /// <summary>Exit pages (last pageview of each session).</summary>
    Task<IReadOnlyList<DimensionRow>> GetExitPagesAsync(ReportScope scope, int limit, CancellationToken ct = default);

    Task<IReadOnlyList<CustomEventRow>> GetCustomEventsAsync(ReportScope scope, int limit, CancellationToken ct = default);
}

public sealed class ReportQueries(IClickHouseConnectionFactory connections, ISiteMetricsQueries siteMetrics) : IReportQueries
{
    // When imported history is merged, both sides fetch more rows so the merged top list is accurate.
    private const int MergedTopListCandidates = 100;

    private sealed class KeyValueRow
    {
        public string Key { get; set; } = "";
        public long Value { get; set; }
    }

    private sealed class EventRow
    {
        public string Name { get; set; } = "";
        public long Count { get; set; }
        public long Visitors { get; set; }
    }

    private sealed class LiveSeriesRow
    {
        public DateTime Date { get; set; }
        public long Visitors { get; set; }
        public long Pageviews { get; set; }
        public long Events { get; set; }
        public long Sessions { get; set; }
        public long Bounces { get; set; }
        public double AvgSessionDuration { get; set; }
    }

    // Live event column and imported dimension name (imported_dimension_stats uses the same names as dimension_stats).
    private static (string Column, string? ImportedName) Map(Dimension dimension) => dimension switch
    {
        Dimension.Path => ("path", "path"),
        Dimension.Referrer => ("referrer_domain", "referrer"),
        Dimension.Country => ("country_code", "country"),
        Dimension.Device => ("device_type", "device"),
        Dimension.Browser => ("browser", "browser"),
        Dimension.Os => ("os", "os"),
        Dimension.UtmSource => ("utm_source", null),
        Dimension.UtmMedium => ("utm_medium", null),
        Dimension.UtmCampaign => ("utm_campaign", null),
        _ => throw new ArgumentOutOfRangeException(nameof(dimension))
    };

    /// <summary>
    /// The live part of a top list, up to the WHERE clause (filters, GROUP BY and LIMIT are appended). Pages count
    /// page views only; device, browser and OS count unique visitors, like the imported totals they merge with;
    /// the rest count events. Internal "leave" events count in none of them.
    /// </summary>
    public static string LiveDimensionSql(Dimension dimension)
    {
        var (column, _) = Map(dimension);
        var (value, condition) = dimension switch
        {
            Dimension.Path => ("count()", $"event_name = '{EventNames.Pageview}'"),
            Dimension.Device or Dimension.Browser or Dimension.Os => ("uniqExact(visitor_id)", $"event_name != '{EventNames.Leave}'"),
            _ => ("count()", $"event_name != '{EventNames.Leave}'")
        };
        return $@"
            SELECT {column} as Key, {value} as Value FROM tempoo_analytics.events
            WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end AND {condition}";
    }

    /// <summary>Device, browser and OS lists count visitors; the other lists count views.</summary>
    private static bool CountsVisitors(Dimension dimension) =>
        dimension is Dimension.Device or Dimension.Browser or Dimension.Os;

    private static DynamicParameters Parameters(ReportScope scope)
    {
        var parameters = new DynamicParameters();
        parameters.Add("siteIds", scope.SiteIds.ToArray());
        parameters.Add("start", scope.Start);
        parameters.Add("end", scope.End);
        return parameters;
    }

    private async Task<ImportWindow?> WindowAsync(ReportScope scope) =>
        scope.Options.IncludeImportedData && scope.Filter is not { HasConditions: true } && scope.SiteIds.Count > 0
            ? await siteMetrics.GetImportWindowAsync(scope.SiteIds, scope.Start, scope.End)
            : null;

    public async Task<IReadOnlyList<SeriesPoint>> GetSeriesAsync(ReportScope scope, SeriesInterval interval, CancellationToken ct = default)
    {
        if (scope.SiteIds.Count == 0) return [];
        var inner = interval switch
        {
            SeriesInterval.Hour => "toStartOfHour(timestamp)",
            SeriesInterval.Week => "toStartOfWeek(timestamp, 1)",
            SeriesInterval.Month => "toStartOfMonth(timestamp)",
            _ => "date"
        };
        var sql = new StringBuilder($@"
            SELECT
                Date,
                uniqExact(visitor_id) as Visitors,
                sum(session_pageviews) as Pageviews,
                sum(session_events) as Events,
                count() as Sessions,
                sum(is_bounced) as Bounces,
                round(if(isFinite(avg(session_duration)), avg(session_duration), 0), 2) as AvgSessionDuration
            FROM (
                SELECT
                    {inner} as Date,
                    session_id,
                    any(visitor_id) as visitor_id,
                    countIf(event_name = 'pageview') as session_pageviews,
                    countIf({EventNames.CustomEventCondition}) as session_events,
                    if({EventNames.BouncedSessionCondition}, 1, 0) as is_bounced,
                    if(count() > 1, least(toUInt32(max(timestamp) - min(timestamp)), 1800), 0) as session_duration
                FROM tempoo_analytics.events
                WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end
        ");
        var parameters = Parameters(scope);
        scope.Filter?.AppendTo(sql, parameters);
        sql.Append(" GROUP BY Date, session_id) GROUP BY Date ORDER BY Date");

        using var conn = connections.Create();
        var liveTask = conn.QueryTracedAsync<LiveSeriesRow>(sql.ToString(), parameters, ct);

        // Imported history has one row per day, so there's nothing to add to an hourly series.
        var window = interval == SeriesInterval.Hour ? null : await WindowAsync(scope);
        if (window == null) return Merge(await liveTask, []);

        var bucket = interval switch
        {
            SeriesInterval.Week => "toStartOfWeek(i.date, 1)",
            SeriesInterval.Month => "toStartOfMonth(i.date)",
            _ => "i.date"
        };
        using var importedConn = connections.Create();
        var importedSql = $@"
            SELECT
                {bucket} AS Date,
                sum(i.visitors) AS Visitors,
                sum(i.pageviews) AS Pageviews,
                sum(i.visits) AS Visits,
                sum(i.bounces) AS Bounces,
                sum(i.visit_duration) AS VisitDuration
            FROM tempoo_analytics.imported_daily_stats AS i
            {SiteMetricsQueries.ImportedRowsFilter}
            GROUP BY Date
            ORDER BY Date";
        var importedTask = importedConn.QueryTracedAsync<ImportedTotalsRow>(importedSql, SiteMetricsQueries.WindowParameters(window), ct);
        await Task.WhenAll(liveTask, importedTask);
        return Merge(liveTask.Result, importedTask.Result);
    }

    /// <summary>Adds imported rows to live ones per bucket. Imported visitors can't be deduplicated across days.</summary>
    private static IReadOnlyList<SeriesPoint> Merge(IEnumerable<LiveSeriesRow> live, IEnumerable<ImportedTotalsRow> imported)
    {
        var buckets = new SortedDictionary<DateTime, (long Visitors, long Pageviews, long Events, long Sessions, long Bounces, double Seconds)>();
        foreach (var row in live)
        {
            buckets.TryGetValue(row.Date, out var b);
            buckets[row.Date] = (b.Visitors + row.Visitors, b.Pageviews + row.Pageviews, b.Events + row.Events,
                b.Sessions + row.Sessions, b.Bounces + row.Bounces, b.Seconds + row.AvgSessionDuration * row.Sessions);
        }
        foreach (var row in imported)
        {
            buckets.TryGetValue(row.Date, out var b);
            buckets[row.Date] = (b.Visitors + row.Visitors, b.Pageviews + row.Pageviews, b.Events,
                b.Sessions + row.Visits, b.Bounces + row.Bounces, b.Seconds + row.VisitDuration);
        }
        return buckets.Select(kv => new SeriesPoint(kv.Key, kv.Value.Visitors, kv.Value.Pageviews, kv.Value.Events,
            kv.Value.Sessions, kv.Value.Bounces,
            kv.Value.Sessions > 0 ? Math.Round(kv.Value.Seconds / kv.Value.Sessions, 2) : 0)).ToList();
    }

    public async Task<IReadOnlyList<DimensionRow>> GetDimensionAsync(ReportScope scope, Dimension dimension, int limit, CancellationToken ct = default)
    {
        if (scope.SiteIds.Count == 0) return [];
        var (_, importedName) = Map(dimension);
        var window = importedName != null ? await WindowAsync(scope) : null;
        if (window == null)
            return (await LiveDimensionAsync(scope, dimension, limit, ct)).Select(x => new DimensionRow(x.Key, x.Value)).ToList();

        var liveTask = LiveDimensionAsync(scope, dimension, MergedTopListCandidates, ct);
        var importedTask = ImportedDimensionAsync(window, importedName!, CountsVisitors(dimension), MergedTopListCandidates, ct);
        await Task.WhenAll(liveTask, importedTask);
        return ImportedData.MergeDimension(
                liveTask.Result.ToDictionary(x => x.Key, x => x.Value),
                importedTask.Result.ToDictionary(x => x.Key, x => x.Value), limit)
            .Select(kv => new DimensionRow(kv.Key, kv.Value)).ToList();
    }

    private async Task<List<KeyValueRow>> LiveDimensionAsync(ReportScope scope, Dimension dimension, int limit, CancellationToken ct)
    {
        using var conn = connections.Create();
        var sql = new StringBuilder(LiveDimensionSql(dimension));
        var parameters = Parameters(scope);
        scope.Filter?.AppendTo(sql, parameters);
        sql.Append($" GROUP BY Key ORDER BY Value DESC LIMIT {limit}");
        return (await conn.QueryTracedAsync<KeyValueRow>(sql.ToString(), parameters, ct)).ToList();
    }

    private async Task<List<KeyValueRow>> ImportedDimensionAsync(ImportWindow window, string dimensionName, bool visitors, int limit, CancellationToken ct)
    {
        using var conn = connections.Create();
        // Live lists count views (pageviews are the closest imported metric, visitors if a table has none) or
        // visitors (device, browser, OS).
        var value = visitors ? "sum(i.visitors)" : "sum(if(i.pageviews > 0, i.pageviews, i.visitors))";
        var sql = $@"
            SELECT i.dimension_value AS Key, {value} AS Value
            FROM tempoo_analytics.imported_dimension_stats AS i
            {SiteMetricsQueries.ImportedRowsFilter}
              AND i.dimension_name = @dimensionName
            GROUP BY Key
            ORDER BY Value DESC
            LIMIT @limit";
        var parameters = SiteMetricsQueries.WindowParameters(window);
        parameters.Add("dimensionName", dimensionName);
        parameters.Add("limit", limit);
        return (await conn.QueryTracedAsync<KeyValueRow>(sql, parameters, ct)).ToList();
    }

    public async Task<IReadOnlyList<DimensionRow>> GetChannelsAsync(ReportScope scope, CancellationToken ct = default)
    {
        if (scope.SiteIds.Count == 0) return [];
        using var conn = connections.Create();
        var sql = new StringBuilder(@"
            SELECT Channel as Key, count() as Value FROM (
                SELECT 
                    multiIf(
                        referrer_domain = '' OR referrer_domain IS NULL, 'Direct',
                        match(referrer_domain, 'google|bing|yahoo|duckduckgo|baidu|ecosia|yandex'), 'Organic Search',
                        match(referrer_domain, 'facebook|instagram|twitter|t.co|linkedin|pinterest|reddit|tiktok|youtube|twitch'), 'Organic Social',
                        traffic_type = 2 OR match(referrer_domain, 'openai|chatgpt|anthropic|claude|perplexity|perplexity.ai|gemini.google.com'), 'AI Tools',
                        'Referral'
                    ) as Channel
                FROM tempoo_analytics.events
                WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end AND event_name != 'leave'
        ");
        var parameters = Parameters(scope);
        scope.Filter?.AppendTo(sql, parameters);
        sql.Append(") GROUP BY Channel ORDER BY Value DESC");
        return (await conn.QueryTracedAsync<KeyValueRow>(sql.ToString(), parameters, ct)).Select(x => new DimensionRow(x.Key, x.Value)).ToList();
    }

    public Task<IReadOnlyList<DimensionRow>> GetEntryPagesAsync(ReportScope scope, int limit, CancellationToken ct = default) =>
        SessionPagesAsync(scope, "argMin", limit, ct);

    public Task<IReadOnlyList<DimensionRow>> GetExitPagesAsync(ReportScope scope, int limit, CancellationToken ct = default) =>
        SessionPagesAsync(scope, "argMax", limit, ct);

    private async Task<IReadOnlyList<DimensionRow>> SessionPagesAsync(ReportScope scope, string aggregate, int limit, CancellationToken ct)
    {
        if (scope.SiteIds.Count == 0) return [];
        using var conn = connections.Create();
        var sql = new StringBuilder($@"
            SELECT PagePath as Key, count() as Value
            FROM (
                SELECT {aggregate}(path, timestamp) as PagePath
                FROM tempoo_analytics.events
                WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end AND event_name = 'pageview'
        ");
        var parameters = Parameters(scope);
        scope.Filter?.AppendTo(sql, parameters);
        sql.Append($" GROUP BY session_id) GROUP BY Key ORDER BY Value DESC LIMIT {limit}");
        return (await conn.QueryTracedAsync<KeyValueRow>(sql.ToString(), parameters, ct)).Select(x => new DimensionRow(x.Key, x.Value)).ToList();
    }

    /// <summary>The custom events list up to the WHERE clause: page views and internal leave events are not custom events.</summary>
    public const string CustomEventsSql = @"
            SELECT event_name as Name, count() as Count, uniqExact(visitor_id) as Visitors
            FROM tempoo_analytics.events
            WHERE site_id IN @siteIds AND " + EventNames.CustomEventCondition + @" AND timestamp >= @start AND timestamp < @end";

    public async Task<IReadOnlyList<CustomEventRow>> GetCustomEventsAsync(ReportScope scope, int limit, CancellationToken ct = default)
    {
        if (scope.SiteIds.Count == 0) return [];
        using var conn = connections.Create();
        var sql = new StringBuilder(CustomEventsSql);
        var parameters = Parameters(scope);
        scope.Filter?.AppendTo(sql, parameters);
        sql.Append($" GROUP BY Name ORDER BY Count DESC LIMIT {limit}");
        return (await conn.QueryTracedAsync<EventRow>(sql.ToString(), parameters, ct)).Select(x => new CustomEventRow(x.Name, x.Count, x.Visitors)).ToList();
    }
}
