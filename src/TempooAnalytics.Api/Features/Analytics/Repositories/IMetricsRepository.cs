using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Repositories;

public interface IMetricsRepository
{
    Task<IReadOnlyList<SiteSummary>> GetSitesSummaryAsync(UserId userId, CancellationToken ct = default);
    Task<IReadOnlyList<CustomEventMetrics>> GetDetailsCustomEventsDailyAsync(UserId userId, string siteKey, int days = 30, CancellationToken ct = default);
    Task<IReadOnlyList<DailyMetric>> GetDailyMetricsAsync(UserId userId, string siteKey, int days = 30, CancellationToken ct = default);
    Task<IReadOnlyList<DailyMetric>> GetDailyMetricsAsync(UserId userId, string siteKey, DateTime start, DateTime end, CancellationToken ct = default);
    Task<IReadOnlyList<DailyMetric>> GetDailyMetricsBySiteIdAsync(SiteId siteId, int days = 30, CancellationToken ct = default);
    Task<IReadOnlyList<CustomEventGroupResultDto>> GetGroupedCustomEventsAsync(UserId userId, string siteKey, string eventName, int days, IEnumerable<string> groupKeys, IDictionary<string, string>? filters = null, CancellationToken ct = default);
    Task<IReadOnlyList<LocationDto>> GetSiteLocationsAsync(UserId userId, string siteKey, int days = 30, int top = 10, CancellationToken ct = default);
    Task<IReadOnlyList<LocationDto>> GetSiteLocationsBySiteIdAsync(SiteId siteId, int days = 30, int top = 10, CancellationToken ct = default);
    Task<IReadOnlyList<TopPageDto>> GetTopPagesAsync(UserId userId, string siteKey, int days = 30, int top = 10, CancellationToken ct = default);
    Task<IReadOnlyList<TopPageDto>> GetTopPagesBySiteIdAsync(SiteId siteId, int days = 30, int top = 10, CancellationToken ct = default);
    Task<IReadOnlyList<EventSummaryDto>> GetCustomEventCountsAsync(UserId userId, string siteKey, int days = 30, CancellationToken ct = default);
    Task<IReadOnlyList<HourlyActivityDto>> GetHourlyActivityAsync(UserId userId, string siteKey, int days = 30, CancellationToken ct = default);
    Task<IReadOnlyList<HourlyActivityDto>> GetHourlyActivityBySiteIdAsync(SiteId siteId, int days = 30, CancellationToken ct = default);
    Task<SiteMetricsSummaryDto> GetSiteMetricsSummaryAsync(UserId userId, string siteKey, int days = 30, CancellationToken ct = default);
    Task<SiteMetricsSummaryDto> GetSiteMetricsSummaryBySiteIdAsync(SiteId siteId, int days = 30, CancellationToken ct = default);
    Task<IReadOnlyList<DeviceDto>> GetSiteDevicesAsync(UserId userId, string siteKey, int days = 30, CancellationToken ct = default);
    Task<IReadOnlyList<DeviceDto>> GetSiteDevicesBySiteIdAsync(SiteId siteId, int days = 30, int top = 10, CancellationToken ct = default);
}
