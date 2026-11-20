using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Repositories;

public interface IClickHouseRepository
{
    Task<IReadOnlyList<HourlyActivityDto>> GetHourlyActivityAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<DeviceDto>> GetSiteDevicesAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<LocationDto>> GetSiteLocationsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<TopPageDto>> GetTopPagesAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<TopPageDto>> GetPageTitlesAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<TopPageDto>> GetEntryPagesAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<TopPageDto>> GetExitPagesAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<PageDetailDto>> GetTopPagesDetailsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<PageDetailDto>> GetPageTitlesDetailsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<PageDetailDto>> GetEntryPagesDetailsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<PageDetailDto>> GetExitPagesDetailsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<BrowserDto>> GetSiteBrowsersAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<OsDto>> GetSiteOSAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<ReferrerDto>> GetSiteReferrersAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<ChannelDto>> GetSiteChannelsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<ReferrerDetailDto>> GetSiteChannelsDetailsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<ReferrerDetailDto>> GetSiteReferrersDetailsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<ScreenDto>> GetSiteScreensAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<DimensionValueDto>> GetUtmSourcesAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<DimensionValueDto>> GetUtmMediumsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<DimensionValueDto>> GetUtmCampaignsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<HourlyActivityDto>> GetWeeklyTrendsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<SiteMetricsSummaryDto> GetSiteMetricsSummaryAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<TopSitePerformanceDto>> GetTopSitesAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, int topN, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<DailyMetricDto>> GetDailyMetricsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, string? breakdown = null, CancellationToken ct = default);
    Task<IReadOnlyList<EventSummaryDto>> GetCustomEventCountsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, CancellationToken ct = default);
    Task<IReadOnlyList<CustomEventGroupResultDto>> GetGroupedCustomEventsAsync(IEnumerable<SiteId> siteIds, string eventName, DateTime start, DateTime end, string[] groupKeys, Dictionary<string, string>? filters, CancellationToken ct = default);
    Task<IReadOnlyList<TechnicalTrafficDto>> GetTechnicalTrafficStatsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<BotTrafficDto>> GetBotTrafficAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);

    /// <summary>Day of the site's first live event (the imported-history cutoff), or null without events.</summary>
    Task<DateOnly?> GetFirstLiveEventDateAsync(SiteId siteId, CancellationToken ct = default);
    Task<IReadOnlyList<CompanyTrafficDto>> GetCompanyTrafficAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<CustomEventMetrics>> GetCustomEventDetailsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<EventDetailDto>> GetEventDetailsAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, AnalyticsFilterDto? filter = null, int limit = 5000, int offset = 0, CancellationToken ct = default);
    Task<long> GetEventCountAsync(IEnumerable<SiteId> siteIds, DateTime start, DateTime end, CancellationToken ct = default);
    Task<int> CleanupOldDataAsync(SiteId siteId, DateTime cutoffDate, CancellationToken ct = default);
}
