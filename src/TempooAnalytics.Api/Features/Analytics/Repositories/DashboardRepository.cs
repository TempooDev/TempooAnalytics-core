using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Shared.Telemetry;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Dtos.Funnels;
using TempooAnalytics.Api.Infrastructure.Common;

using Microsoft.Extensions.Caching.Hybrid;

namespace TempooAnalytics.Api.Features.Analytics.Repositories;

public class DashboardRepository(
    AnalyticsDbContext dbContext, 
    IClickHouseRepository clickHouseRepo,
    IFunnelRepository funnelRepo,
    HybridCache cache) : IDashboardRepository
{
    public async Task<DashboardDto> GetDashboardAsync(UserId ownerId, DateTime? from = null, DateTime? to = null, int days = 30, int topN = 5, string? siteKey = null, AnalyticsFilterDto? filter = null, string? breakdown = null, CancellationToken ct = default)
    {
        using var act = TelemetryExtensions.StartActivity("repo.dashboard.getDashboard");
        
        var dto = new DashboardDto();

        var sitesQuery = dbContext.AnalyticsSites.AsNoTracking()
            .Where(s => s.OwnerId == ownerId && !s.IsDeleted);
            
        if (!string.IsNullOrWhiteSpace(siteKey)) 
        {
            sitesQuery = sitesQuery.Where(s => s.SiteKey == siteKey);
        }

        var sites = await sitesQuery
            .Select(s => new { s.Id, s.SiteKey, s.Name })
            .ToListAsync(ct);

        if (sites.Count == 0) return dto;

        var siteIds = sites.Select(s => s.Id).ToList();
        dto.TotalSites = sites.Count;

        DateTime start, end;
        if (from.HasValue && to.HasValue)
        {
            start = from.Value;
            end = to.Value;
        }
        else
        {
            if (days <= 0) days = 30;
            var today = DateTime.UtcNow.Date;
            end = today.AddDays(1);
            start = today.AddDays(-(days - 1));
        }
        
        var rangeSpan = end - start;
        var prevEnd = start;
        var prevStart = start.Subtract(rangeSpan);

        // Fetch metrics in parallel from ClickHouse using the calculated range
        var dailyMetricsTask = clickHouseRepo.GetDailyMetricsAsync(siteIds, start, end, filter, breakdown, ct);
        var topPagesTask = clickHouseRepo.GetTopPagesAsync(siteIds, start, end, filter, ct);
        var pageTitlesTask = clickHouseRepo.GetPageTitlesAsync(siteIds, start, end, filter, ct);
        var entryPagesTask = clickHouseRepo.GetEntryPagesAsync(siteIds, start, end, filter, ct);
        var exitPagesTask = clickHouseRepo.GetExitPagesAsync(siteIds, start, end, filter, ct);
        var locationsTask = clickHouseRepo.GetSiteLocationsAsync(siteIds, start, end, filter, ct);
        var devicesTask = clickHouseRepo.GetSiteDevicesAsync(siteIds, start, end, filter, ct);
        var browsersTask = clickHouseRepo.GetSiteBrowsersAsync(siteIds, start, end, filter, ct);
        var osTask = clickHouseRepo.GetSiteOSAsync(siteIds, start, end, filter, ct);
        var referrersTask = clickHouseRepo.GetSiteReferrersAsync(siteIds, start, end, filter, ct);
        var channelsTask = clickHouseRepo.GetSiteChannelsAsync(siteIds, start, end, filter, ct);
        var screensTask = clickHouseRepo.GetSiteScreensAsync(siteIds, start, end, filter, ct);
        var utmSourcesTask = clickHouseRepo.GetUtmSourcesAsync(siteIds, start, end, filter, ct);
        var utmMediumsTask = clickHouseRepo.GetUtmMediumsAsync(siteIds, start, end, filter, ct);
        var utmCampaignsTask = clickHouseRepo.GetUtmCampaignsAsync(siteIds, start, end, filter, ct);
        var weeklyTrendsTask = clickHouseRepo.GetWeeklyTrendsAsync(siteIds, start, end, filter, ct);
        var technicalTrafficTask = clickHouseRepo.GetTechnicalTrafficStatsAsync(siteIds, start, end, filter, ct);
        var botTrafficTask = clickHouseRepo.GetBotTrafficAsync(siteIds, start, end, filter, ct);
        var companyTrafficTask = clickHouseRepo.GetCompanyTrafficAsync(siteIds, start, end, filter, ct);
        var customEventsTask = clickHouseRepo.GetCustomEventCountsAsync(siteIds, start, end, ct);
        var summaryTask = clickHouseRepo.GetSiteMetricsSummaryAsync(siteIds, start, end, filter, ct);
        var summaryPrevTask = clickHouseRepo.GetSiteMetricsSummaryAsync(siteIds, prevStart, prevEnd, filter, ct);
        var topSitesTask = clickHouseRepo.GetTopSitesAsync(siteIds, start, end, topN, filter, ct);

        // Fetch funnels definitions
        Task<List<FunnelDefinitionDto>> funnelDefsTask = (siteIds.Count == 1) 
            ? funnelRepo.GetFunnelsBySiteAsync(siteIds[0], ct)
            : Task.FromResult(new List<FunnelDefinitionDto>());

        await Task.WhenAll(
            dailyMetricsTask, topPagesTask, pageTitlesTask, entryPagesTask, exitPagesTask, 
            locationsTask, devicesTask, 
            browsersTask, osTask, referrersTask, channelsTask, screensTask,
            utmSourcesTask, utmMediumsTask, utmCampaignsTask,
            weeklyTrendsTask, technicalTrafficTask, botTrafficTask, companyTrafficTask, customEventsTask, 
            summaryTask, summaryPrevTask, topSitesTask, funnelDefsTask
        );

        dto.DailyMetrics = GapFiller.FillDailyMetricsGaps(await dailyMetricsTask, start, end, breakdown);
        dto.TopPages = await topPagesTask;
        dto.PageTitles = await pageTitlesTask;
        dto.EntryPages = await entryPagesTask;
        dto.ExitPages = await exitPagesTask;
        dto.Locations = await locationsTask;
        dto.Devices = await devicesTask;
        dto.Browsers = await browsersTask;
        dto.OS = await osTask;
        dto.Referrers = await referrersTask;
        dto.Channels = await channelsTask;
        dto.Screens = await screensTask;
        dto.UtmSources = await utmSourcesTask;
        dto.UtmMediums = await utmMediumsTask;
        dto.UtmCampaigns = await utmCampaignsTask;
        dto.WeeklyTrends = await weeklyTrendsTask;
        dto.TechnicalTraffic = await technicalTrafficTask;
        dto.BotTraffic = await botTrafficTask;
        dto.Companies = await companyTrafficTask;
        dto.CustomEvents = await customEventsTask;
        
        var funnels = await funnelDefsTask;
        if (funnels.Count > 0 && siteIds.Count == 1)
        {
            // Calculate actual conversion for each funnel
            var siteId = siteIds[0];
            foreach (var f in funnels)
            {
                // Note: funnel calculation now supports custom from/to
                var result = await funnelRepo.CalculateFunnelAsync(siteId, f.Steps, start, end, ct);
                f.ConversionRate = result.OverallConversionRate;
                f.TotalSessions = result.Steps.FirstOrDefault()?.Sessions ?? 0;
            }
        }
        dto.Funnels = funnels;

        var summary = await summaryTask;
        dto.IncludesImportedData = summary.IncludesImportedData;
        dto.ImportedDataExcludedByFilter = summary.ImportedDataExcludedByFilter;
        dto.Metrics30 = new MetricCardDto
        {
            Pageviews30 = summary.Pageviews,
            Visitors30 = summary.Visitors,
            Sessions30 = summary.Sessions,
            Events30 = summary.TotalEvents,
            CustomEvents30 = summary.CustomEvents,
            BounceRate30 = Math.Round(summary.BounceRate, 2),
            PagesPerSession30 = summary.Sessions > 0 ? Math.Round((double)summary.Pageviews / summary.Sessions, 2) : 0,
            AvgSessionDuration30 = Math.Round(summary.AvgSessionDuration, 2)
        };

        var summaryPrev = await summaryPrevTask;
        dto.Metrics30Previous = new MetricCardDto
        {
            Pageviews30 = summaryPrev.Pageviews,
            Visitors30 = summaryPrev.Visitors,
            Sessions30 = summaryPrev.Sessions,
            Events30 = summaryPrev.TotalEvents,
            CustomEvents30 = summaryPrev.CustomEvents,
            BounceRate30 = Math.Round(summaryPrev.BounceRate, 2),
            PagesPerSession30 = summaryPrev.Sessions > 0 ? Math.Round((double)summaryPrev.Pageviews / summaryPrev.Sessions, 2) : 0,
            AvgSessionDuration30 = Math.Round(summaryPrev.AvgSessionDuration, 2)
        };

        var topPerformance = await topSitesTask;
        var sitesDict = sites.ToDictionary(s => s.Id, s => s);

        dto.TopSites = topPerformance.Select(p => new TopSiteDto
        {
            SiteId = p.SiteId,
            SiteKey = sitesDict.TryGetValue(new SiteId(p.SiteId), out var s) ? s.SiteKey : "unknown",
            Name = sitesDict.TryGetValue(new SiteId(p.SiteId), out var s2) ? s2.Name : "Unknown",
            Pageviews = p.Pageviews,
            Visitors = p.Visitors,
            Events = p.Events,
            Active = true
        }).ToList();

        return dto;
    }

    public async Task<IReadOnlyList<ReferrerDetailDto>> GetReferrersDetailsAsync(UserId ownerId, DateTime? from = null, DateTime? to = null, int days = 30, string? siteKey = null, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        var siteIds = await GetTargetSiteIdsAsync(ownerId, siteKey, ct);
        if (siteIds.Count == 0) return Array.Empty<ReferrerDetailDto>();

        var (start, end) = GetRange(from, to, days);
        return await clickHouseRepo.GetSiteReferrersDetailsAsync(siteIds, start, end, filter, ct);
    }

    public async Task<IReadOnlyList<ReferrerDetailDto>> GetChannelsDetailsAsync(UserId ownerId, DateTime? from = null, DateTime? to = null, int days = 30, string? siteKey = null, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        var siteIds = await GetTargetSiteIdsAsync(ownerId, siteKey, ct);
        if (siteIds.Count == 0) return Array.Empty<ReferrerDetailDto>();

        var (start, end) = GetRange(from, to, days);
        return await clickHouseRepo.GetSiteChannelsDetailsAsync(siteIds, start, end, filter, ct);
    }

    public async Task<IReadOnlyList<PageDetailDto>> GetPagesDetailsAsync(UserId ownerId, string type, DateTime? from = null, DateTime? to = null, int days = 30, string? siteKey = null, AnalyticsFilterDto? filter = null, CancellationToken ct = default)
    {
        var siteIds = await GetTargetSiteIdsAsync(ownerId, siteKey, ct);
        if (siteIds.Count == 0) return Array.Empty<PageDetailDto>();

        var (start, end) = GetRange(from, to, days);
        return type.ToLower() switch
        {
            "pages" => await clickHouseRepo.GetTopPagesDetailsAsync(siteIds, start, end, filter, ct),
            "titles" => await clickHouseRepo.GetPageTitlesDetailsAsync(siteIds, start, end, filter, ct),
            "entry" => await clickHouseRepo.GetEntryPagesDetailsAsync(siteIds, start, end, filter, ct),
            "exit" => await clickHouseRepo.GetExitPagesDetailsAsync(siteIds, start, end, filter, ct),
            _ => await clickHouseRepo.GetTopPagesDetailsAsync(siteIds, start, end, filter, ct)
        };
    }

    private async Task<List<SiteId>> GetTargetSiteIdsAsync(UserId ownerId, string? siteKey, CancellationToken ct)
    {
        var sitesQuery = dbContext.AnalyticsSites.AsNoTracking()
            .Where(s => s.OwnerId == ownerId && !s.IsDeleted);

        if (!string.IsNullOrWhiteSpace(siteKey))
        {
            sitesQuery = sitesQuery.Where(s => s.SiteKey == siteKey);
        }

        return await sitesQuery.Select(s => s.Id).ToListAsync(ct);
    }

    private (DateTime Start, DateTime End) GetRange(DateTime? from, DateTime? to, int days)
    {
        if (from.HasValue && to.HasValue) return (from.Value, to.Value);
        
        if (days <= 0) days = 30;
        var today = DateTime.UtcNow.Date;
        var end = today.AddDays(1);
        var start = today.AddDays(-(days - 1));
        return (start, end);
    }

    public async Task<IReadOnlyList<EventDetailDto>> GetEventsDetailsAsync(UserId ownerId, DateTime? from = null, DateTime? to = null, int days = 30, string? siteKey = null, AnalyticsFilterDto? filter = null, int limit = 100, int offset = 0, CancellationToken ct = default)
    {
        var siteIds = await GetTargetSiteIdsAsync(ownerId, siteKey, ct);
        if (siteIds.Count == 0) return Array.Empty<EventDetailDto>();

        var (start, end) = GetRange(from, to, days);
        
        // A range is considered closed if a "to" date was provided and it is not today (or in the future)
        bool isClosedRange = to.HasValue && to.Value < DateTime.UtcNow.Date;
        
        if (isClosedRange)
        {
            var cacheKey = $"events_{siteKey ?? "all"}_{start:yyyyMMdd}_{end:yyyyMMdd}";
            var cachedEvents = await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await clickHouseRepo.GetEventDetailsAsync(siteIds, start, end, null, 5000, 0, cancel),
                cancellationToken: ct
            );
            
            IEnumerable<EventDetailDto> query = cachedEvents;
            
            if (filter?.AdvancedFilters != null && filter.AdvancedFilters.Any())
            {
                foreach (var f in filter.AdvancedFilters)
                {
                    if (f.Values == null || f.Values.Count == 0) continue;
                    
                    query = f.Property.ToLowerInvariant() switch
                    {
                        "path" => ApplyFilter(query, x => x.Url, f.Operator, f.Values),
                        "country" => ApplyFilter(query, x => x.CountryCode, f.Operator, f.Values),
                        "browser" => ApplyFilter(query, x => x.Browser, f.Operator, f.Values),
                        "os" => ApplyFilter(query, x => x.OS, f.Operator, f.Values),
                        "device" => ApplyFilter(query, x => x.DeviceType, f.Operator, f.Values),
                        "event name" => ApplyFilter(query, x => x.EventName, f.Operator, f.Values),
                        "traffic type" => ApplyFilter(query, x => x.TrafficType, f.Operator, f.Values),
                        "city" => ApplyFilter(query, x => x.CityName, f.Operator, f.Values),
                        "user alias" => ApplyFilter(query, x => GenerateAlias(x.VisitorId), f.Operator, f.Values),
                        "user_alias" => ApplyFilter(query, x => GenerateAlias(x.VisitorId), f.Operator, f.Values),
                        _ => query
                    };
                }
            }
            
            var results = query.Skip(offset).Take(limit).ToList();
            results.ForEach(x => x.UserAlias = GenerateAlias(x.VisitorId));
            return results;
        }
        else
        {
            // If there is a filter by alias, we need to fetch more data and filter in memory
            // because the alias is a computed field that ClickHouse does not know about.
            bool hasAliasFilter = filter?.AdvancedFilters?.Any(f => f.Property.ToLowerInvariant() == "user alias" || f.Property.ToLowerInvariant() == "user_alias") ?? false;
            
            if (hasAliasFilter)
            {
                // Clone filters excluding "user alias" for ClickHouse
                var clickHouseFilter = new AnalyticsFilterDto
                {
                    DeviceType = filter!.DeviceType,
                    OS = filter.OS,
                    Browser = filter.Browser,
                    CountryCode = filter.CountryCode,
                    Path = filter.Path,
                    ReferrerDomain = filter.ReferrerDomain,
                    UtmSource = filter.UtmSource,
                    UtmMedium = filter.UtmMedium,
                    UtmCampaign = filter.UtmCampaign,
                    AdvancedFilters = filter.AdvancedFilters?.Where(f => f.Property.ToLowerInvariant() != "user alias" && f.Property.ToLowerInvariant() != "user_alias").ToList() ?? []
                };

                var liveEvents = await clickHouseRepo.GetEventDetailsAsync(siteIds, start, end, clickHouseFilter, 5000, 0, ct);
                IEnumerable<EventDetailDto> query = liveEvents;
                
                var aliasFilters = filter!.AdvancedFilters!.Where(f => f.Property.ToLowerInvariant() == "user alias" || f.Property.ToLowerInvariant() == "user_alias");
                foreach (var f in aliasFilters)
                {
                    query = ApplyFilter(query, x => GenerateAlias(x.VisitorId), f.Operator, f.Values);
                }
                
                var results = query.Skip(offset).Take(limit).ToList();
                results.ForEach(x => x.UserAlias = GenerateAlias(x.VisitorId));
                return results;
            }
            else
            {
                var results = await clickHouseRepo.GetEventDetailsAsync(siteIds, start, end, filter, limit, offset, ct);
                foreach (var r in results) r.UserAlias = GenerateAlias(r.VisitorId);
                return results;
            }
        }
    }

    private static readonly string[] Adjectives = { "Swift", "Brave", "Quiet", "Bright", "Steady", "Clever", "Calm", "Wild", "Kind", "Bold", "Noble", "Lucky", "Happy", "Wise", "Fast", "Sly", "Magic", "Cool", "Zen", "Sharp" };
    private static readonly string[] Animals = { "Panda", "Tiger", "Eagle", "Fox", "Dolphin", "Lion", "Wolf", "Owl", "Bear", "Falcon", "Koala", "Otter", "Shark", "Deer", "Lynx", "Cat", "Dog", "Horse", "Rabbit", "Seal" };

    private string GenerateAlias(string id)
    {
        if (string.IsNullOrEmpty(id)) return "Anonymous";
        
        uint hash = 0;
        foreach (char c in id) hash = (hash << 5) - hash + c;

        var adj = Adjectives[hash % (uint)Adjectives.Length];
        var anim = Animals[(hash >> 8) % (uint)Animals.Length];
        return $"{adj} {anim}";
    }

    private IEnumerable<EventDetailDto> ApplyFilter(IEnumerable<EventDetailDto> source, Func<EventDetailDto, string> selector, string op, List<string> values)
    {
        return op.ToLowerInvariant() switch
        {
            "is" => source.Where(x => values.Contains(selector(x), StringComparer.OrdinalIgnoreCase)),
            "is_not" => source.Where(x => !values.Contains(selector(x), StringComparer.OrdinalIgnoreCase)),
            "contains" => source.Where(x => values.Any(v => selector(x)?.Contains(v, StringComparison.OrdinalIgnoreCase) == true)),
            "not_contains" => source.Where(x => !values.Any(v => selector(x)?.Contains(v, StringComparison.OrdinalIgnoreCase) == true)),
            _ => source
        };
    }
}
