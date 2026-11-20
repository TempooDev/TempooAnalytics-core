using System.Collections.Concurrent;
using System.Data;
using System.Text;
using ClickHouse.Client.ADO;
using Dapper;
using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Analytics.Query;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.FeatureFlags;
using TempooAnalytics.Api.Infrastructure.Tracing;

namespace TempooAnalytics.Api.Features.Analytics.Repositories;

public class ClickHouseRepository(IConfiguration configuration, IFeatureFlags featureFlags, ISiteMetricsQueries siteMetricsQueries, IReportQueries reportQueries) : IClickHouseRepository
{
    private readonly string _connectionString = configuration.GetConnectionString("clickhouse") 
                            ?? throw new InvalidOperationException("ClickHouse connection string is missing");

    private IDbConnection CreateConnection() => new ClickHouseConnection(_connectionString);

    private const int TopListSize = 20;
    // Imported history: the window lookup and the row filter live in the shared query library, so the
    // dashboard and the public API apply the same cutoff rules. The library instance is scoped like this
    // repository, so the dashboard's concurrent queries still share one window lookup.
    private const string ImportedRowsFilter = SiteMetricsQueries.ImportedRowsFilter;

    private static DynamicParameters WindowParameters(ImportWindow window) => SiteMetricsQueries.WindowParameters(window);

    private Task<ImportWindow?> GetImportWindowAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end) =>
        siteMetricsQueries.GetImportWindowAsync(siteIds.Select(x => x.Value), start, end);

    private async Task<IReadOnlyList<ImportedTotalsRow>> GetImportedDailyTotalsAsync(ImportWindow window, string bucket)
    {
        using var conn = CreateConnection();
        var sql = $@"
            SELECT
                {bucket} AS Date,
                sum(i.visitors) AS Visitors,
                sum(i.pageviews) AS Pageviews,
                sum(i.visits) AS Visits,
                sum(i.bounces) AS Bounces,
                sum(i.visit_duration) AS VisitDuration
            FROM tempoo_analytics.imported_daily_stats AS i
            {ImportedRowsFilter}
            GROUP BY Date
            ORDER BY Date";
        return [.. await conn.QueryWithTracingAsync<ImportedTotalsRow>(sql, WindowParameters(window))];
    }

    private bool ShouldIncludeImported(AnalyticsFilterDto? filter) =>
        featureFlags.PlausibleImport && !ImportedDataMerger.HasFilters(filter);

    public async Task<IReadOnlyList<HourlyActivityDto>> GetHourlyActivityAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return Array.Empty<HourlyActivityDto>();
        return await GetHourlyActivityFromEventsAsync(siteIds, start, end, filter, ct);
    }

    // The SQL of these reports lives in TempooAnalytics.Analytics.Query, shared with the public API.
    private ReportScope ScopeOf(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter) =>
        new(siteIds.Select(x => x.Value).ToArray(), start, end,
            filter == null ? null : new EventFilter(ImportedDataMerger.HasFilters(filter), (sql, parameters) => AppendFilters(sql, filter, parameters)),
            new QueryOptions(IncludeImportedData: featureFlags.PlausibleImport));

    private static Dimension? DimensionOf(string column) => column switch
    {
        "path" => Dimension.Path,
        "referrer_domain" => Dimension.Referrer,
        "country_code" => Dimension.Country,
        "device_type" => Dimension.Device,
        "browser" => Dimension.Browser,
        "os" => Dimension.Os,
        _ => null
    };

    private async Task<IReadOnlyList<T>> GetTypedDimensionStatsAsync<T>(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, string dimensionColumn, AnalyticsFilterDto? filter = null, Func<string, long, T>? mapper = null, CancellationToken ct = default)
    {
        if (mapper == null) return Array.Empty<T>();
        if (DimensionOf(dimensionColumn) is { } dimension)
        {
            var rows = await reportQueries.GetDimensionAsync(ScopeOf(siteIds, start, end, filter), dimension, TopListSize, ct);
            return rows.Select(x => mapper(x.Value, x.Views)).ToList();
        }
        // Columns without imported history (page titles) are live only, and count page views like the pages list.
        var stats = await GetDimensionStatsFromEventsAsync(siteIds, start, end, dimensionColumn, filter, ct, TopListSize, pageviewsOnly: true);
        return stats.Select(x => mapper(x.Key, x.Value)).ToList();
    }

    public async Task<IReadOnlyList<DeviceDto>> GetSiteDevicesAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        return await GetTypedDimensionStatsAsync(siteIds, start, end, "device_type", filter, (k, v) => new DeviceDto { DeviceType = string.IsNullOrEmpty(k) ? "Unknown" : k, Value = v }, ct);
    }

    public async Task<IReadOnlyList<LocationDto>> GetSiteLocationsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        return await GetTypedDimensionStatsAsync(siteIds, start, end, "country_code", filter, (k, v) => new LocationDto { CountryCode = k, Value = v }, ct);
    }

    public async Task<IReadOnlyList<TopPageDto>> GetTopPagesAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        return await GetTypedDimensionStatsAsync(siteIds, start, end, "path", filter, (k, v) => new TopPageDto { Url = k, Views = (int)v }, ct);
    }

    public async Task<IReadOnlyList<TopPageDto>> GetPageTitlesAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        return await GetTypedDimensionStatsAsync(siteIds, start, end, "page_title", filter, (k, v) => new TopPageDto { Url = string.IsNullOrEmpty(k) ? "Untitled" : k, Views = (int)v }, ct);
    }

    public async Task<IReadOnlyList<TopPageDto>> GetEntryPagesAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default) =>
        (await reportQueries.GetEntryPagesAsync(ScopeOf(siteIds, start, end, filter), TopListSize, ct))
        .Select(x => new TopPageDto { Url = x.Value, Views = (int)x.Views }).ToList();

    public async Task<IReadOnlyList<TopPageDto>> GetExitPagesAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default) =>
        (await reportQueries.GetExitPagesAsync(ScopeOf(siteIds, start, end, filter), TopListSize, ct))
        .Select(x => new TopPageDto { Url = x.Value, Views = (int)x.Views }).ToList();

    public async Task<IReadOnlyList<PageDetailDto>> GetTopPagesDetailsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return Array.Empty<PageDetailDto>();
        using var conn = CreateConnection();
        var sql = new StringBuilder(@"
            SELECT 
                Page,
                count() as Views,
                round(Views / nullIf(sum(Views) OVER (), 0) * 100, 2) as PercentageViews,
                uniqExact(visitor_id) as Visitors,
                0.0 as AvgTimeOnPage,
                0.0 as BounceRate
            FROM (
                SELECT 
                    path as Page,
                    visitor_id
                FROM tempoo_analytics.events
                WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end AND event_name = 'pageview'
        ");
        var parameters = new DynamicParameters();
        parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
        parameters.Add("start", start);
        parameters.Add("end", end);
        AppendFilters(sql, filter, parameters);
        sql.Append(" ) GROUP BY Page ORDER BY Views DESC LIMIT 100");
        return (await conn.QueryWithTracingAsync<PageDetailDto>(sql.ToString(), parameters)).ToList();
    }

    public async Task<IReadOnlyList<PageDetailDto>> GetPageTitlesDetailsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return Array.Empty<PageDetailDto>();
        using var conn = CreateConnection();
        var sql = new StringBuilder(@"
            SELECT 
                if(page_title = '', 'Untitled', page_title) as Page,
                count() as Views,
                round(Views / nullIf(sum(Views) OVER (), 0) * 100, 2) as PercentageViews,
                uniqExact(visitor_id) as Visitors,
                0.0 as AvgTimeOnPage,
                0.0 as BounceRate
            FROM tempoo_analytics.events
            WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end AND event_name = 'pageview'
        ");
        var parameters = new DynamicParameters();
        parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
        parameters.Add("start", start);
        parameters.Add("end", end);
        AppendFilters(sql, filter, parameters);
        sql.Append(" GROUP BY Page ORDER BY Views DESC LIMIT 100");
        return (await conn.QueryWithTracingAsync<PageDetailDto>(sql.ToString(), parameters)).ToList();
    }

    public async Task<IReadOnlyList<PageDetailDto>> GetEntryPagesDetailsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return Array.Empty<PageDetailDto>();
        using var conn = CreateConnection();
        var sql = new StringBuilder($@"
            SELECT 
                Page,
                count() as Views,
                round(Views / nullIf(sum(Views) OVER (), 0) * 100, 2) as PercentageViews,
                uniqExact(visitor_id) as Visitors,
                round(sum(IsBounced) / nullIf(Views, 0) * 100, 2) as BounceRate
            FROM (
                SELECT 
                    argMinIf(path, timestamp, event_name = 'pageview') as Page,
                    any(visitor_id) as visitor_id,
                    if({EventNames.BouncedSessionCondition}, 1, 0) as IsBounced
                FROM tempoo_analytics.events
                WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end
        ");
        var parameters = new DynamicParameters();
        parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
        parameters.Add("start", start);
        parameters.Add("end", end);
        AppendFilters(sql, filter, parameters);
        sql.Append(" GROUP BY session_id HAVING countIf(event_name = 'pageview') >= 1) GROUP BY Page ORDER BY Views DESC LIMIT 100");
        return (await conn.QueryWithTracingAsync<PageDetailDto>(sql.ToString(), parameters)).ToList();
    }

    public async Task<IReadOnlyList<PageDetailDto>> GetExitPagesDetailsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return Array.Empty<PageDetailDto>();
        using var conn = CreateConnection();
        var sql = new StringBuilder(@"
            SELECT 
                Page,
                count() as Views,
                round(Views / nullIf(sum(Views) OVER (), 0) * 100, 2) as PercentageViews,
                uniqExact(visitor_id) as Visitors,
                0.0 as BounceRate
            FROM (
                SELECT 
                    argMax(path, timestamp) as Page,
                    any(visitor_id) as visitor_id
                FROM tempoo_analytics.events
                WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end AND event_name = 'pageview'
        ");
        var parameters = new DynamicParameters();
        parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
        parameters.Add("start", start);
        parameters.Add("end", end);
        AppendFilters(sql, filter, parameters);
        sql.Append(" GROUP BY session_id) GROUP BY Page ORDER BY Views DESC LIMIT 100");
        return (await conn.QueryWithTracingAsync<PageDetailDto>(sql.ToString(), parameters)).ToList();
    }

    public async Task<IReadOnlyList<BrowserDto>> GetSiteBrowsersAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        return await GetTypedDimensionStatsAsync(siteIds, start, end, "browser", filter, (k, v) => new BrowserDto { Browser = string.IsNullOrEmpty(k) ? "Unknown" : k, Value = v }, ct);
    }

    public async Task<IReadOnlyList<OsDto>> GetSiteOSAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        return await GetTypedDimensionStatsAsync(siteIds, start, end, "os", filter, (k, v) => new OsDto { OS = string.IsNullOrEmpty(k) ? "Unknown" : k, Value = v }, ct);
    }

    public async Task<IReadOnlyList<ReferrerDto>> GetSiteReferrersAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        return await GetTypedDimensionStatsAsync(siteIds, start, end, "referrer_domain", filter, (k, v) => new ReferrerDto { Referrer = string.IsNullOrEmpty(k) ? "Direct" : k, Value = v }, ct);
    }

    public async Task<IReadOnlyList<ChannelDto>> GetSiteChannelsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default) =>
        (await reportQueries.GetChannelsAsync(ScopeOf(siteIds, start, end, filter), ct))
        .Select(x => new ChannelDto { Channel = x.Value, Value = x.Views }).ToList();

    public async Task<IReadOnlyList<ReferrerDetailDto>> GetSiteChannelsDetailsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return Array.Empty<ReferrerDetailDto>();
        using var conn = CreateConnection();
        var sql = new StringBuilder($@"
            SELECT 
                Channel as Referrer,
                count() as Sessions,
                sum(Pageviews) as Pageviews,
                round(if(isFinite(Sessions / nullIf(sum(Sessions) OVER (), 0) * 100), Sessions / nullIf(sum(Sessions) OVER (), 0) * 100, 0), 2) as PercentageSessions,
                round(if(isFinite(Pageviews / nullIf(sum(Pageviews) OVER (), 0) * 100), Pageviews / nullIf(sum(Pageviews) OVER (), 0) * 100, 0), 2) as PercentagePageviews,
                round(if(isFinite(sum(IsBounced) / nullIf(Sessions, 0) * 100), sum(IsBounced) / nullIf(Sessions, 0) * 100, 0), 2) as BounceRate
            FROM (
                SELECT 
                    session_id,
                    any(multiIf(
                        referrer_domain = '' OR referrer_domain IS NULL, 'Direct',
                        match(referrer_domain, 'google|bing|yahoo|duckduckgo|baidu|ecosia|yandex'), 'Organic Search',
                        match(referrer_domain, 'facebook|instagram|twitter|t.co|linkedin|pinterest|reddit|tiktok|youtube|twitch'), 'Organic Social',
                        traffic_type = 2 OR match(referrer_domain, 'openai|chatgpt|anthropic|claude|perplexity|perplexity.ai|gemini.google.com'), 'AI Tools',
                        'Referral'
                    )) as Channel,
                    countIf(event_name = 'pageview') as Pageviews,
                    if({EventNames.BouncedSessionCondition}, 1, 0) as IsBounced
                FROM tempoo_analytics.events
                WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end
        ");

        var parameters = new DynamicParameters();
        parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
        parameters.Add("start", start);
        parameters.Add("end", end);

        AppendFilters(sql, filter, parameters);

        sql.Append(@"
                GROUP BY session_id
            )
            GROUP BY Channel
            ORDER BY Sessions DESC
            LIMIT 100
        ");

        return (await conn.QueryWithTracingAsync<ReferrerDetailDto>(sql.ToString(), parameters)).ToList();
    }

    public async Task<IReadOnlyList<ReferrerDetailDto>> GetSiteReferrersDetailsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return Array.Empty<ReferrerDetailDto>();
        using var conn = CreateConnection();
        var sql = new StringBuilder($@"
            SELECT 
                Referrer,
                count() as Sessions,
                sum(Pageviews) as Pageviews,
                round(if(isFinite(Sessions / nullIf(sum(Sessions) OVER (), 0) * 100), Sessions / nullIf(sum(Sessions) OVER (), 0) * 100, 0), 2) as PercentageSessions,
                round(if(isFinite(Pageviews / nullIf(sum(Pageviews) OVER (), 0) * 100), Pageviews / nullIf(sum(Pageviews) OVER (), 0) * 100, 0), 2) as PercentagePageviews,
                round(if(isFinite(sum(IsBounced) / nullIf(Sessions, 0) * 100), sum(IsBounced) / nullIf(Sessions, 0) * 100, 0), 2) as BounceRate
            FROM (
                SELECT 
                    session_id,
                    any(if(referrer_domain = '', 'Direct', referrer_domain)) as Referrer,
                    countIf(event_name = 'pageview') as Pageviews,
                    if({EventNames.BouncedSessionCondition}, 1, 0) as IsBounced
                FROM tempoo_analytics.events
                WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end
        ");

        var parameters = new DynamicParameters();
        parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
        parameters.Add("start", start);
        parameters.Add("end", end);

        AppendFilters(sql, filter, parameters);

        sql.Append(@"
                GROUP BY session_id
            )
            GROUP BY Referrer
            ORDER BY Sessions DESC
            LIMIT 100
        ");

        return (await conn.QueryWithTracingAsync<ReferrerDetailDto>(sql.ToString(), parameters)).ToList();
    }

    public async Task<IReadOnlyList<ScreenDto>> GetSiteScreensAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        var stats = await GetDimensionStatsFromEventsAsync(siteIds, start, end, "concat(toString(screen_width), 'x', toString(screen_height))", filter, ct);
        return stats.Select(x => new ScreenDto { Resolution = x.Key, Value = x.Value }).ToList();
    }

    public async Task<IReadOnlyList<DimensionValueDto>> GetUtmSourcesAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default) =>
        (await reportQueries.GetDimensionAsync(ScopeOf(siteIds, start, end, filter), Dimension.UtmSource, TopListSize, ct))
        .Select(x => new DimensionValueDto { Name = string.IsNullOrEmpty(x.Value) ? "None" : x.Value, Value = x.Views }).ToList();

    public async Task<IReadOnlyList<DimensionValueDto>> GetUtmMediumsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default) =>
        (await reportQueries.GetDimensionAsync(ScopeOf(siteIds, start, end, filter), Dimension.UtmMedium, TopListSize, ct))
        .Select(x => new DimensionValueDto { Name = string.IsNullOrEmpty(x.Value) ? "None" : x.Value, Value = x.Views }).ToList();

    public async Task<IReadOnlyList<DimensionValueDto>> GetUtmCampaignsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default) =>
        (await reportQueries.GetDimensionAsync(ScopeOf(siteIds, start, end, filter), Dimension.UtmCampaign, TopListSize, ct))
        .Select(x => new DimensionValueDto { Name = string.IsNullOrEmpty(x.Value) ? "None" : x.Value, Value = x.Views }).ToList();

    public async Task<IReadOnlyList<HourlyActivityDto>> GetWeeklyTrendsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return Array.Empty<HourlyActivityDto>();
        using var conn = CreateConnection();
        var sql = new StringBuilder(@"
            SELECT toDayOfWeek(timestamp) as DayOfWeek, toHour(timestamp) as Hour, count() as Count
            FROM tempoo_analytics.events WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end");
        var parameters = new DynamicParameters();
        parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
        parameters.Add("start", start);
        parameters.Add("end", end);
        AppendFilters(sql, filter, parameters);
        sql.Append(" GROUP BY DayOfWeek, Hour ORDER BY DayOfWeek, Hour");
        return [.. (await conn.QueryWithTracingAsync<HourlyActivityDto>(sql.ToString(), parameters))];
    }

    public async Task<IReadOnlyList<TopSitePerformanceDto>> GetTopSitesAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, int topN, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return Array.Empty<TopSitePerformanceDto>();
        using var conn = CreateConnection();
        var sql = new StringBuilder(@"
            SELECT site_id as SiteId, countIf(event_name = 'pageview') as Pageviews, uniqExact(visitor_id) as Visitors, countIf(event_name NOT IN ('pageview', 'leave')) as Events
            FROM tempoo_analytics.events WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end");
        var parameters = new DynamicParameters();
        parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
        parameters.Add("start", start);
        parameters.Add("end", end);
        parameters.Add("topN", topN);
        AppendFilters(sql, filter, parameters);
        sql.Append(" GROUP BY SiteId ORDER BY Pageviews DESC LIMIT @topN");
        return (await conn.QueryWithTracingAsync<TopSitePerformanceDto>(sql.ToString(), parameters)).ToList();
    }

    public Task<SiteMetricsSummaryDto> GetSiteMetricsSummaryAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        // The SQL lives in TempooAnalytics.Analytics.Query, shared with the public API.
        var eventFilter = filter == null
            ? null
            : new EventFilter(ImportedDataMerger.HasFilters(filter), (sql, parameters) => AppendFilters(sql, filter, parameters));
        return siteMetricsQueries.GetSummaryAsync(
            siteIds.Select(x => x.Value).ToArray(), start, end, eventFilter,
            new QueryOptions(IncludeImportedData: featureFlags.PlausibleImport), ct);
    }

    public async Task<IReadOnlyList<DailyMetricDto>> GetDailyMetricsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, string? breakdown = null, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return Array.Empty<DailyMetricDto>();
        using var conn = CreateConnection();
        
        // Determine the time grouping function (FOR INNER QUERY)
        string timeFunctionInner;
        if (!string.IsNullOrEmpty(breakdown))
        {
            timeFunctionInner = breakdown.ToLower() switch
            {
                "hour" => "toStartOfHour(timestamp)",
                "day" => "toStartOfDay(timestamp)",
                "week" => "toStartOfWeek(timestamp, 1)",
                "month" => "toStartOfMonth(timestamp)",
                _ => "date"
            };
        }
        else
        {
            // Default heuristic if no breakdown provided
            // Must match GapFiller.FillDailyMetricsGaps, which fills the same buckets.
            timeFunctionInner = GapFiller.IsHourlyByDefault(start, end) ? "toStartOfHour(timestamp)" : "date";
        }

        var sql = new StringBuilder($@"
            SELECT
                Date,
                uniqExact(visitor_id) as Visitors,
                sum(session_pageviews) as Pageviews,
                sum(session_events) as Events,
                count() as Sessions,
                round(if(isFinite(avg(session_duration)), avg(session_duration), 0), 2) as AvgSessionDuration
            FROM (
                SELECT
                    {timeFunctionInner} as Date,
                    session_id,
                    any(visitor_id) as visitor_id,
                    min(timestamp) as min_ts,
                    countIf(event_name = 'pageview') as session_pageviews,
                    countIf(event_name NOT IN ('pageview', 'leave')) as session_events,
                    if(count() > 1, least(toUInt32(max(timestamp) - min(timestamp)), 1800), 0) as session_duration
                FROM tempoo_analytics.events
                WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end
        ");
        var parameters = new DynamicParameters();
        parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
        parameters.Add("start", start);
        parameters.Add("end", end);

        AppendFilters(sql, filter, parameters);
        sql.Append(" GROUP BY Date, session_id) GROUP BY Date ORDER BY Date");
        var liveTask = conn.QueryWithTracingAsync<LiveDailyMetricRow>(sql.ToString(), parameters);

        // Imported history has one row per day, so there's nothing to add to an hourly chart.
        var isHourlyChart = timeFunctionInner == "toStartOfHour(timestamp)";
        var window = isHourlyChart || !ShouldIncludeImported(filter) ? null : await GetImportWindowAsync(siteIds, start, end);
        if (window == null)
        {
            return ImportedDataMerger.MergeDaily(await liveTask, []);
        }

        var importedBucket = breakdown?.ToLower() switch
        {
            "week" => "toStartOfWeek(i.date, 1)",
            "month" => "toStartOfMonth(i.date)",
            _ => "i.date"
        };
        var importedTask = GetImportedDailyTotalsAsync(window, importedBucket);
        await Task.WhenAll(liveTask, importedTask);
        return ImportedDataMerger.MergeDaily(liveTask.Result, importedTask.Result);
    }

    public async Task<IReadOnlyList<EventSummaryDto>> GetCustomEventCountsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, CancellationToken ct = default) =>
        (await reportQueries.GetCustomEventsAsync(ScopeOf(siteIds, start, end, null), int.MaxValue, ct))
        .Select(x => new EventSummaryDto { EventName = x.Name, Count = x.Count }).ToList();

    public async Task<IReadOnlyList<CustomEventGroupResultDto>> GetGroupedCustomEventsAsync(IEnumerable<SiteId> siteIds, string eventName, DateTime start, DateTime end, string[] groupKeys, Dictionary<string, string>? filters, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return Array.Empty<CustomEventGroupResultDto>();
        using var conn = CreateConnection();
        var results = new List<CustomEventGroupResultDto>();
        foreach (var key in groupKeys)
        {
            var sql = $@"
                SELECT JSONExtractString(data, @key) as Value, count() as Count
                FROM tempoo_analytics.events 
                WHERE site_id IN @siteIds AND event_name = @eventName AND timestamp >= @start AND timestamp < @end AND JSONHas(data, @key)
                GROUP BY Value ORDER BY Count DESC LIMIT 20";
            var keyResults = await conn.QueryWithTracingAsync<(string Value, long Count)>(sql, new { siteIds = siteIds.Select(x => x.Value).ToArray(), eventName, start, end, key });
            results.AddRange(keyResults.Select(x => new CustomEventGroupResultDto(key, x.Value, x.Count)));
        }
        return results;
    }

    public async Task<IReadOnlyList<TechnicalTrafficDto>> GetTechnicalTrafficStatsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return Array.Empty<TechnicalTrafficDto>();
        using var conn = CreateConnection();
        var sql = new StringBuilder(@"
            SELECT multiIf(traffic_type = 1, 'Human', traffic_type = 2, 'Bot IA', traffic_type = 3, 'Crawler', traffic_type = 4, 'VPN/Proxy', 'Other') as Type, count() as Count
            FROM tempoo_analytics.events WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end");
        var parameters = new DynamicParameters();
        parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
        parameters.Add("start", start);
        parameters.Add("end", end);
        AppendFilters(sql, filter, parameters);
        sql.Append(" GROUP BY Type ORDER BY Count DESC");
        return (await conn.QueryWithTracingAsync<TechnicalTrafficDto>(sql.ToString(), parameters)).ToList();
    }

    public async Task<DateOnly?> GetFirstLiveEventDateAsync(SiteId siteId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        // GROUP BY so a site without events returns no row (a bare min() would return 1970-01-01).
        var firstLive = (await conn.QueryWithTracingAsync<DateTime>(@"
            SELECT min(date)
            FROM tempoo_analytics.events
            WHERE site_id = @siteId
            GROUP BY site_id", new { siteId = siteId.Value })).ToList();
        return firstLive.Count == 0 ? null : DateOnly.FromDateTime(firstLive[0]);
    }

    public async Task<IReadOnlyList<BotTrafficDto>> GetBotTrafficAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return Array.Empty<BotTrafficDto>();
        try {
            using var conn = CreateConnection();
            var sql = new StringBuilder(@"
                SELECT bot_name as Name, count() as Count
                FROM tempoo_analytics.events 
                WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end AND traffic_type IN (2, 3) AND bot_name != ''");
            var parameters = new DynamicParameters();
            parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
            parameters.Add("start", start);
            parameters.Add("end", end);
            AppendFilters(sql, filter, parameters);
            sql.Append(" GROUP BY Name ORDER BY Count DESC LIMIT 20");
            return (await conn.QueryWithTracingAsync<BotTrafficDto>(sql.ToString(), parameters)).ToList();
        } catch (Exception) {
            // Fallback if columns don't exist yet
            return Array.Empty<BotTrafficDto>();
        }
    }

    public async Task<IReadOnlyList<CompanyTrafficDto>> GetCompanyTrafficAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return Array.Empty<CompanyTrafficDto>();
        try {
            using var conn = CreateConnection();
            var sql = new StringBuilder(@"
                SELECT company_name as Name, company_domain as Domain, count() as Count
                FROM tempoo_analytics.events 
                WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end AND company_name != ''");
            var parameters = new DynamicParameters();
            parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
            parameters.Add("start", start);
            parameters.Add("end", end);
            AppendFilters(sql, filter, parameters);
            sql.Append(" GROUP BY Name, Domain ORDER BY Count DESC LIMIT 20");
            return (await conn.QueryWithTracingAsync<CompanyTrafficDto>(sql.ToString(), parameters)).ToList();
        } catch (Exception) {
            // Fallback if columns don't exist yet
            return Array.Empty<CompanyTrafficDto>();
        }
    }

    public async Task<IReadOnlyList<CustomEventMetrics>> GetCustomEventDetailsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return Array.Empty<CustomEventMetrics>();
        using var conn = CreateConnection();
        var sql = new StringBuilder(@"
            SELECT timestamp as Date, event_name as EventName, data as Data, toString(session_id) as SessionId, visitor_id as VisitorId, path as Url
            FROM tempoo_analytics.events WHERE site_id IN @siteIds AND event_name NOT IN ('pageview', 'leave') AND timestamp >= @start AND timestamp < @end");
        var parameters = new DynamicParameters();
        parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
        parameters.Add("start", start);
        parameters.Add("end", end);
        AppendFilters(sql, filter, parameters);
        sql.Append(" ORDER BY timestamp DESC LIMIT 1000");
        return (await conn.QueryWithTracingAsync<CustomEventMetrics>(sql.ToString(), parameters)).ToList();
    }

    public async Task<IReadOnlyList<EventDetailDto>> GetEventDetailsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, int limit = 5000, int offset = 0, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return Array.Empty<EventDetailDto>();
        using var conn = CreateConnection();
        var sql = new StringBuilder(@"
            SELECT 
                timestamp as Timestamp, 
                event_name as EventName, 
                toString(traffic_type) as TrafficType, 
                country_code as CountryCode, 
                city_name as CityName, 
                device_type as DeviceType, 
                os as OS, 
                browser as Browser, 
                path as Url, 
                data as Data,
                toString(session_id) as SessionId,
                visitor_id as VisitorId
            FROM tempoo_analytics.events 
            WHERE site_id IN @siteIds 
              AND timestamp >= @start 
              AND timestamp < @end");
              
        var parameters = new DynamicParameters();
        parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
        parameters.Add("start", start);
        parameters.Add("end", end);
        AppendFilters(sql, filter, parameters);
        sql.Append(" ORDER BY timestamp DESC LIMIT @limit OFFSET @offset");
        parameters.Add("limit", limit);
        parameters.Add("offset", offset);
        
        return (await conn.QueryWithTracingAsync<EventDetailDto>(sql.ToString(), parameters)).ToList();
    }

    private async Task<IReadOnlyList<HourlyActivityDto>> GetHourlyActivityFromEventsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter, CancellationToken ct)
    {
        using var conn = CreateConnection();
        var sql = new StringBuilder(@"
            SELECT toDayOfWeek(date) as DayOfWeek, toHour(timestamp) as Hour, count() as Count
            FROM tempoo_analytics.events WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end");
        var parameters = new DynamicParameters();
        parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
        parameters.Add("start", start);
        parameters.Add("end", end);
        AppendFilters(sql, filter, parameters);
        sql.Append(" GROUP BY DayOfWeek, Hour ORDER BY DayOfWeek, Hour");
        return (await conn.QueryWithTracingAsync<HourlyActivityDto>(sql.ToString(), parameters)).ToList();
    }

    private async Task<IDictionary<string, long>> GetDimensionStatsFromEventsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, string dimensionColumn, AnalyticsFilterDto? filter, CancellationToken ct, int limit = TopListSize, bool pageviewsOnly = false)
    {
        using var conn = CreateConnection();
        var eventCondition = pageviewsOnly ? "event_name = 'pageview'" : "event_name != 'leave'";
        var sql = new StringBuilder($@"
            SELECT {dimensionColumn} as Key, count() as Value FROM tempoo_analytics.events
            WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end AND {eventCondition}");
        var parameters = new DynamicParameters();
        parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
        parameters.Add("start", start);
        parameters.Add("end", end);
        AppendFilters(sql, filter, parameters);
        sql.Append($" GROUP BY Key ORDER BY Value DESC LIMIT {limit}");
        var results = await conn.QueryWithTracingAsync<(string Key, long Value)>(sql.ToString(), parameters);
        return results.ToDictionary(x => x.Key, x => x.Value);
    }

    private void AppendFilters(StringBuilder sql, AnalyticsFilterDto? filter, DynamicParameters parameters)
    {
        if (filter == null) return;
        if (!string.IsNullOrEmpty(filter.DeviceType)) AddFilter(sql, "device_type", filter.DeviceType, parameters);
        if (!string.IsNullOrEmpty(filter.OS)) AddFilter(sql, "os", filter.OS, parameters);
        if (!string.IsNullOrEmpty(filter.Browser)) AddFilter(sql, "browser", filter.Browser, parameters);
        if (!string.IsNullOrEmpty(filter.CountryCode)) AddFilter(sql, "country_code", filter.CountryCode, parameters);
        if (!string.IsNullOrEmpty(filter.Path)) AddFilter(sql, "path", filter.Path, parameters);
        if (!string.IsNullOrEmpty(filter.ReferrerDomain)) AddFilter(sql, "referrer_domain", filter.ReferrerDomain, parameters);
        if (!string.IsNullOrEmpty(filter.UtmSource)) AddFilter(sql, "utm_source", filter.UtmSource, parameters);
        if (!string.IsNullOrEmpty(filter.UtmMedium)) AddFilter(sql, "utm_medium", filter.UtmMedium, parameters);
        if (!string.IsNullOrEmpty(filter.UtmCampaign)) AddFilter(sql, "utm_campaign", filter.UtmCampaign, parameters);
        
        if (filter.AdvancedFilters != null && filter.AdvancedFilters.Any())
        {
            foreach (var adv in filter.AdvancedFilters)
            {
                var column = MapPropertyToColumn(adv.Property);
                if (string.IsNullOrEmpty(column)) continue;
                
                // Ensure paramName is safe for SQL
                var safeColumn = new string(column.Where(c => char.IsLetterOrDigit(c)).ToArray());
                var paramName = $"adv_{safeColumn}_{Guid.NewGuid():N}";
                switch (adv.Operator.ToLower())
                {
                    case "is":
                        if (!adv.Values.Any()) break;
                        var inParams = new List<string>();
                        for (int i = 0; i < adv.Values.Count; i++) { var p = $"{paramName}_{i}"; inParams.Add($"@{p}"); parameters.Add(p, adv.Values[i]); }
                        sql.Append($" AND {column} IN ({string.Join(", ", inParams)})");
                        break;
                    case "is_not":
                        if (!adv.Values.Any()) break;
                        var notInParams = new List<string>();
                        for (int i = 0; i < adv.Values.Count; i++) { var p = $"{paramName}_{i}"; notInParams.Add($"@{p}"); parameters.Add(p, adv.Values[i]); }
                        sql.Append($" AND {column} NOT IN ({string.Join(", ", notInParams)})");
                        break;
                    case "contains":
                        var containsConditions = new List<string>();
                        for (int i = 0; i < adv.Values.Count; i++) { var p = $"{paramName}_{i}"; containsConditions.Add($"{column} LIKE @{p}"); parameters.Add(p, $"%{adv.Values[i]}%"); }
                        if (containsConditions.Any()) sql.Append($" AND ({string.Join(" OR ", containsConditions)})");
                        break;
                    case "not_contains":
                        var notContainsConditions = new List<string>();
                        for (int i = 0; i < adv.Values.Count; i++) { var p = $"{paramName}_{i}"; notContainsConditions.Add($"{column} NOT LIKE @{p}"); parameters.Add(p, $"%{adv.Values[i]}%"); }
                        if (notContainsConditions.Any()) sql.Append($" AND ({string.Join(" AND ", notContainsConditions)})");
                        break;
                    case "matches":
                        if (adv.Values.Any()) { sql.Append($" AND match({column}, @{paramName})"); parameters.Add(paramName, adv.Values.First()); }
                        break;
                    case "not_matches":
                         if (adv.Values.Any()) { sql.Append($" AND NOT match({column}, @{paramName})"); parameters.Add(paramName, adv.Values.First()); }
                        break;
                }
            }
        }
    }

    private string? MapPropertyToColumn(string property) => property.ToLower() switch { "path" => "path", "page title" => "page_title", "country" => "country_code", "browser" => "browser", "os" => "os", "device" => "device_type", "referrer" => "referrer", "event name" => "event_name", "user id" => "visitor_id", "traffic type" => "traffic_type", "city" => "city_name", "user alias" => null, "user_alias" => null, _ => null };
    private void AddFilter(StringBuilder sql, string column, string value, DynamicParameters parameters) { var paramName = column.Replace("_", ""); sql.Append($" AND {column} = @{paramName}"); parameters.Add(paramName, value); }

    public async Task<long> GetEventCountAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, CancellationToken ct = default)
    {
        if (!siteIds.Any()) return 0;
        using var conn = CreateConnection();
        var sql = "SELECT count() FROM tempoo_analytics.events WHERE site_id IN @siteIds AND timestamp >= @start AND timestamp < @end";
        var parameters = new DynamicParameters();
        parameters.Add("siteIds", siteIds.Select(x => x.Value).ToArray());
        parameters.Add("start", start);
        parameters.Add("end", end);
        var count = await conn.ExecuteScalarWithTracingAsync<long?>(new CommandDefinition(sql, parameters, cancellationToken: ct));
        return count ?? 0;
    }

    public async Task<int> CleanupOldDataAsync(SiteId siteId, DateTime cutoffDate, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        // ClickHouse DELETE is an asynchronous mutation
        var sql = "ALTER TABLE tempoo_analytics.events DELETE WHERE site_id = @SiteId AND date < @CutoffDate";
        return await conn.ExecuteWithTracingAsync(new CommandDefinition(sql, new { SiteId = siteId.Value, CutoffDate = cutoffDate.Date }, cancellationToken: ct));
    }
}
