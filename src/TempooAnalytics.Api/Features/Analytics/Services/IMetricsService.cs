using TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Services;

public interface IMetricsService : IService
{
    Task<IEnumerable<UserSiteDto>> GetSitesSummaryAsync(UserId userId, CancellationToken ct);
    Task<IEnumerable<DailyMetricDto>> GetDailyMetricsAsync(UserId userId, string siteKey, int days, CancellationToken ct);
    Task<IEnumerable<CustomEventMetrics>> GetDetailsCustomEventsDailyAsync(UserId userId, string siteKey, int days, CancellationToken ct);
    Task<IEnumerable<CustomEventGroupResultDto>> GetGroupedCustomEventsAsync(
        UserId userId,
        string siteKey,
        string eventName,
        int days,
        string[] groupKeys,
        Dictionary<string, string>? filters,
        CancellationToken ct);
    Task<IEnumerable<HourlyActivityDto>> GetHourlyActivityAsync(UserId userId, string siteKey, int days, CancellationToken ct);
    Task<SiteMetricsSummaryDto> GetSiteMetricsSummaryAsync(UserId userId, string siteKey, int days, CancellationToken ct);
    Task<IEnumerable<DeviceDto>> GetSiteDevicesAsync(UserId userId, string siteKey, int days, CancellationToken ct);
    Task<IEnumerable<LocationDto>> GetSiteLocationsAsync(UserId userId, string siteKey, int days, int top, CancellationToken ct);
    Task<IEnumerable<TopPageDto>> GetTopPagesAsync(UserId userId, string siteKey, int days, int top, CancellationToken ct);
    Task<IEnumerable<EventSummaryDto>> GetCustomEventCountsAsync(UserId userId, string siteKey, int days, CancellationToken ct);
}
