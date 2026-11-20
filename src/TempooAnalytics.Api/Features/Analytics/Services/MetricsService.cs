using TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Services;

public class MetricsService(IMetricsRepository metricsRepo) : IMetricsService
{
    public async Task<IEnumerable<UserSiteDto>> GetSitesSummaryAsync(UserId userId, CancellationToken ct)
    {
        var sites = await metricsRepo.GetSitesSummaryAsync(userId, ct);
        return sites.Select(s => new UserSiteDto(s.SiteId, s.SiteKey, s.Name, s.WriteKey, s.AllowedOrigins));
    }

    public async Task<IEnumerable<DailyMetricDto>> GetDailyMetricsAsync(UserId userId, string siteKey, int days, CancellationToken ct)
    {
        var metrics = await metricsRepo.GetDailyMetricsAsync(userId, siteKey, days, ct);
        return metrics.Select(m => new DailyMetricDto
        {
            Date = m.Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Pageviews = m.Pageviews,
            Visitors = m.Visitors,
            Events = m.Events,
            AvgSessionDuration = m.AvgSessionDurationSeconds
        });
    }

    public async Task<IEnumerable<CustomEventMetrics>> GetDetailsCustomEventsDailyAsync(UserId userId, string siteKey, int days, CancellationToken ct)
    {
        return await metricsRepo.GetDetailsCustomEventsDailyAsync(userId, siteKey, days, ct);
    }

    public async Task<IEnumerable<CustomEventGroupResultDto>> GetGroupedCustomEventsAsync(
        UserId userId,
        string siteKey,
        string eventName,
        int days,
        string[] groupKeys,
        Dictionary<string, string>? filters,
        CancellationToken ct)
    {
        return await metricsRepo.GetGroupedCustomEventsAsync(userId, siteKey, eventName, days, groupKeys, filters, ct);
    }

    public async Task<IEnumerable<HourlyActivityDto>> GetHourlyActivityAsync(UserId userId, string siteKey, int days, CancellationToken ct)
    {
        return await metricsRepo.GetHourlyActivityAsync(userId, siteKey, days, ct);
    }

    public async Task<SiteMetricsSummaryDto> GetSiteMetricsSummaryAsync(UserId userId, string siteKey, int days, CancellationToken ct)
    {
        return await metricsRepo.GetSiteMetricsSummaryAsync(userId, siteKey, days, ct);
    }

    public async Task<IEnumerable<DeviceDto>> GetSiteDevicesAsync(UserId userId, string siteKey, int days, CancellationToken ct)
    {
        return await metricsRepo.GetSiteDevicesAsync(userId, siteKey, days, ct);
    }

    public async Task<IEnumerable<LocationDto>> GetSiteLocationsAsync(UserId userId, string siteKey, int days, int top, CancellationToken ct)
    {
        return await metricsRepo.GetSiteLocationsAsync(userId, siteKey, days, top, ct);
    }

    public async Task<IEnumerable<TopPageDto>> GetTopPagesAsync(UserId userId, string siteKey, int days, int top, CancellationToken ct)
    {
        return await metricsRepo.GetTopPagesAsync(userId, siteKey, days, top, ct);
    }

    public async Task<IEnumerable<EventSummaryDto>> GetCustomEventCountsAsync(UserId userId, string siteKey, int days, CancellationToken ct)
    {
        return await metricsRepo.GetCustomEventCountsAsync(userId, siteKey, days, ct);
    }
}
