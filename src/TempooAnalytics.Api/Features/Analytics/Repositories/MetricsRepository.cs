using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Shared.Telemetry;

namespace TempooAnalytics.Api.Features.Analytics.Repositories;

public class MetricsRepository(
    AnalyticsDbContext dbContext, 
    IClickHouseRepository clickHouseRepo) : IMetricsRepository
{
    public async Task<IReadOnlyList<SiteSummary>> GetSitesSummaryAsync(UserId userId, CancellationToken ct = default)
    {
        using var act = TelemetryExtensions.StartActivity("repo.metrics.getSitesSummary");
        
        // The site list summary will default to 7 days for the "sparkline" or mini-summary
        const int DefaultDays = 7;
        var (start, end) = GetRange(DefaultDays);

        var sites = await dbContext.AnalyticsSites
            .AsNoTracking()
            .Where(s => s.OwnerId == userId && !s.IsDeleted)
            .Select(s => new { s.Id, s.SiteKey, s.Name, s.WriteKey, s.AllowedOrigins, s.CreatedAt, s.PublicShareToken })
            .ToListAsync(ct);

        if (sites.Count == 0) return Array.Empty<SiteSummary>();

        var siteIds = sites.Select(s => s.Id).ToList();
        var performance = await clickHouseRepo.GetTopSitesAsync(siteIds, start, end, sites.Count, null, ct);
        var perfDict = performance.ToDictionary(p => p.SiteId, p => p);

        return sites.Select(s => new SiteSummary
        {
            SiteId = s.Id.Value,
            SiteKey = s.SiteKey,
            Name = s.Name,
            WriteKey = s.WriteKey,
            AllowedOrigins = s.AllowedOrigins,
            PublicShareToken = s.PublicShareToken,
            TotalEvents = perfDict.TryGetValue(s.Id.Value, out var p) ? p.Events : 0,
            Pageviews = perfDict.TryGetValue(s.Id.Value, out var p2) ? p2.Pageviews : 0,
            Visitors = perfDict.TryGetValue(s.Id.Value, out var p3) ? p3.Visitors : 0
        }).ToList();
    }

    public async Task<IReadOnlyList<CustomEventMetrics>> GetDetailsCustomEventsDailyAsync(UserId userId, string siteKey, int days = 30, CancellationToken ct = default)
    {
        var site = await GetSiteAsync(userId, siteKey, ct);
        if (site == null) return Array.Empty<CustomEventMetrics>();
        var (start, end) = GetRange(days);
        return await clickHouseRepo.GetCustomEventDetailsAsync(new[] { site.Id }, start, end, null, ct);
    }

    public async Task<IReadOnlyList<DailyMetric>> GetDailyMetricsAsync(UserId userId, string siteKey, int days = 30, CancellationToken ct = default)
    {
        var site = await GetSiteAsync(userId, siteKey, ct);
        if (site == null) return Array.Empty<DailyMetric>();

        return await GetDailyMetricsBySiteIdAsync(site.Id, days, ct);
    }

    public async Task<IReadOnlyList<DailyMetric>> GetDailyMetricsAsync(UserId userId, string siteKey, DateTime start, DateTime end, CancellationToken ct = default)
    {
        var site = await GetSiteAsync(userId, siteKey, ct);
        if (site == null) return Array.Empty<DailyMetric>();

        var results = await clickHouseRepo.GetDailyMetricsAsync(new[] { site.Id }, start, end, null, null, ct);

        return results.Select(r => new DailyMetric
        {
            Day = DateOnly.FromDateTime(r.Date),
            Pageviews = r.Pageviews,
            Visitors = r.Visitors,
            Events = r.Events,
            AvgSessionDurationSeconds = r.AvgSessionDuration
        }).ToList();
    }

    public async Task<IReadOnlyList<DailyMetric>> GetDailyMetricsBySiteIdAsync(SiteId siteId, int days = 30, CancellationToken ct = default)
    {
        var (start, end) = GetRange(days);
        var results = await clickHouseRepo.GetDailyMetricsAsync(new[] { siteId }, start, end, null, null, ct);

        return results.Select(r => new DailyMetric
        {
            Day = DateOnly.FromDateTime(r.Date),
            Pageviews = r.Pageviews,
            Visitors = r.Visitors,
            Events = r.Events,
            AvgSessionDurationSeconds = r.AvgSessionDuration
        }).ToList();
    }

    public async Task<IReadOnlyList<CustomEventGroupResultDto>> GetGroupedCustomEventsAsync(UserId userId, string siteKey, string eventName, int days, IEnumerable<string> groupKeys, IDictionary<string, string>? filters = null, CancellationToken ct = default)
    {
        var site = await GetSiteAsync(userId, siteKey, ct);
        if (site == null) return Array.Empty<CustomEventGroupResultDto>();
        var (start, end) = GetRange(days);
        return await clickHouseRepo.GetGroupedCustomEventsAsync(new[] { site.Id }, eventName, start, end, groupKeys.ToArray(), filters?.ToDictionary(x => x.Key, x => x.Value), ct);
    }

    public async Task<IReadOnlyList<LocationDto>> GetSiteLocationsAsync(UserId userId, string siteKey, int days = 30, int top = 10, CancellationToken ct = default)
    {
        var site = await GetSiteAsync(userId, siteKey, ct);
        if (site == null) return Array.Empty<LocationDto>();
        var (start, end) = GetRange(days);
        return await clickHouseRepo.GetSiteLocationsAsync(new[] { site.Id }, start, end, null, ct);
    }

    public async Task<IReadOnlyList<LocationDto>> GetSiteLocationsBySiteIdAsync(SiteId siteId, int days = 30, int top = 10, CancellationToken ct = default)
    {
        var (start, end) = GetRange(days);
        return await clickHouseRepo.GetSiteLocationsAsync(new[] { siteId }, start, end, null, ct);
    }

    public async Task<IReadOnlyList<TopPageDto>> GetTopPagesAsync(UserId userId, string siteKey, int days = 30, int top = 10, CancellationToken ct = default)
    {
        var site = await GetSiteAsync(userId, siteKey, ct);
        if (site == null) return Array.Empty<TopPageDto>();
        var (start, end) = GetRange(days);
        return await clickHouseRepo.GetTopPagesAsync(new[] { site.Id }, start, end, null, ct);
    }

    public async Task<IReadOnlyList<EventSummaryDto>> GetCustomEventCountsAsync(UserId userId, string siteKey, int days = 30, CancellationToken ct = default)
    {
        var site = await GetSiteAsync(userId, siteKey, ct);
        if (site == null) return Array.Empty<EventSummaryDto>();
        var (start, end) = GetRange(days);
        return await clickHouseRepo.GetCustomEventCountsAsync(new[] { site.Id }, start, end, ct);
    }

    public async Task<IReadOnlyList<TopPageDto>> GetTopPagesBySiteIdAsync(SiteId siteId, int days = 30, int top = 10, CancellationToken ct = default)
    {
        var (start, end) = GetRange(days);
        return await clickHouseRepo.GetTopPagesAsync(new[] { siteId }, start, end, null, ct);
    }

    public async Task<IReadOnlyList<HourlyActivityDto>> GetHourlyActivityAsync(UserId userId, string siteKey, int days = 30, CancellationToken ct = default)
    {
        var site = await GetSiteAsync(userId, siteKey, ct);
        if (site == null) return Array.Empty<HourlyActivityDto>();
        var (start, end) = GetRange(days);
        return await clickHouseRepo.GetHourlyActivityAsync(new[] { site.Id }, start, end, null, ct);
    }

    public async Task<IReadOnlyList<HourlyActivityDto>> GetHourlyActivityBySiteIdAsync(SiteId siteId, int days = 30, CancellationToken ct = default)
    {
        var (start, end) = GetRange(days);
        return await clickHouseRepo.GetHourlyActivityAsync(new[] { siteId }, start, end, null, ct);
    }

    public async Task<SiteMetricsSummaryDto> GetSiteMetricsSummaryAsync(UserId userId, string siteKey, int days = 30, CancellationToken ct = default)
    {
        var site = await GetSiteAsync(userId, siteKey, ct);
        if (site == null) return new SiteMetricsSummaryDto();
        var (start, end) = GetRange(days);
        return await clickHouseRepo.GetSiteMetricsSummaryAsync(new[] { site.Id }, start, end, null, ct);
    }

    public async Task<SiteMetricsSummaryDto> GetSiteMetricsSummaryBySiteIdAsync(SiteId siteId, int days = 30, CancellationToken ct = default)
    {
        var (start, end) = GetRange(days);
        return await clickHouseRepo.GetSiteMetricsSummaryAsync(new[] { siteId }, start, end, null, ct);
    }

    public async Task<IReadOnlyList<DeviceDto>> GetSiteDevicesAsync(UserId userId, string siteKey, int days = 30, CancellationToken ct = default)
    {
        var site = await GetSiteAsync(userId, siteKey, ct);
        if (site == null) return Array.Empty<DeviceDto>();
        var (start, end) = GetRange(days);
        return await clickHouseRepo.GetSiteDevicesAsync(new[] { site.Id }, start, end, null, ct);
    }

    public async Task<IReadOnlyList<DeviceDto>> GetSiteDevicesBySiteIdAsync(SiteId siteId, int days = 30, int top = 10, CancellationToken ct = default)
    {
        var (start, end) = GetRange(days);
        return await clickHouseRepo.GetSiteDevicesAsync(new[] { siteId }, start, end, null, ct);
    }

    private async Task<AnalyticsSite?> GetSiteAsync(UserId userId, string siteKey, CancellationToken ct)
    {
        // Compare the whole SiteId: EF translates it through the value converter, but not `s.Id.Value`.
        var sites = dbContext.AnalyticsSites.AsNoTracking().Where(s => s.OwnerId == userId);
        if (Guid.TryParse(siteKey, out var siteIdUuid))
        {
            var siteId = new SiteId(siteIdUuid);
            return await sites.FirstOrDefaultAsync(s => s.Id == siteId || s.SiteKey == siteKey, ct);
        }
        return await sites.FirstOrDefaultAsync(s => s.SiteKey == siteKey, ct);
    }

    private static (DateTime Start, DateTime End) GetRange(int days)
    {
        if (days <= 0) days = 30;
        var today = DateTime.UtcNow.Date;
        var end = today.AddDays(1);
        var start = today.AddDays(-(days - 1));
        return (start, end);
    }
}
