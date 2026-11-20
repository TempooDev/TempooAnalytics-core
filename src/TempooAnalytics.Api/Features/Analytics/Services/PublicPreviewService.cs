using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Services;

public class PublicPreviewService(AnalyticsDbContext db, IDistributedCache cache, IClickHouseRepository clickHouseRepo, IFunnelRepository funnelRepo, ISiteAccessService siteAccess) : IPublicPreviewService
{
    public async Task<string?> GeneratePublicTokenAsync(UserId userId, string siteKey, CancellationToken ct)
    {
        AnalyticsSite? site = null;
        if (Guid.TryParse(siteKey, out var siteGuid))
        {
            // Compare the whole SiteId: EF translates it through the value converter, but not `s.Id.Value`.
            var siteId = new SiteId(siteGuid);
            site = await db.AnalyticsSites.FirstOrDefaultAsync(s => s.Id == siteId && !s.IsDeleted, ct);
        }

        if (site == null)
        {
            site = await db.AnalyticsSites.FirstOrDefaultAsync(s => s.SiteKey == siteKey && !s.IsDeleted, ct);
        }

        // Site keys are public (they're in the tracking snippet), so only users with access may share a site.
        // Same answer as a missing site, so the endpoint doesn't reveal which sites exist.
        if (site == null || !await siteAccess.CanAccessAsync(userId, site.Id, ct)) return null;

        if (string.IsNullOrEmpty(site.PublicShareToken))
        {
            site.PublicShareToken = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync(ct);
        }

        return site.PublicShareToken;
    }

    public async Task<PublicPreviewResponse?> GetPublicPreviewDataAsync(string token, DateTime? from = null, DateTime? to = null, int days = 30, string? breakdown = null, CancellationToken ct = default)
    {
        var cacheKey = $"public_preview:{token}_{from?.Ticks ?? 0}_{to?.Ticks ?? 0}_{days}_{breakdown}";
        var cachedData = await cache.GetStringAsync(cacheKey, ct);

        if (!string.IsNullOrEmpty(cachedData))
        {
            return JsonSerializer.Deserialize(cachedData, typeof(PublicPreviewResponse), AppJsonContext.Default) as PublicPreviewResponse;
        }

        var site = await db.AnalyticsSites
            .FirstOrDefaultAsync(s => s.PublicShareToken == token, ct);

        if (site == null) return null;

        var (start, end) = GetRange(from, to, days);
        var rangeSpan = end - start;
        var prevEnd = start;
        var prevStart = start.Subtract(rangeSpan);
        
        var dailyMetricsRaw = await clickHouseRepo.GetDailyMetricsAsync(new[] { site.Id }, start, end, null, breakdown, ct);
        var filledMetrics = GapFiller.FillDailyMetricsGaps(dailyMetricsRaw, start, end, breakdown);
        
        var topPagesTask = clickHouseRepo.GetTopPagesAsync(new[] { site.Id }, start, end, null, ct);
        var hourlyActivityTask = clickHouseRepo.GetHourlyActivityAsync(new[] { site.Id }, start, end, null, ct);
        var summaryTask = clickHouseRepo.GetSiteMetricsSummaryAsync(new[] { site.Id }, start, end, null, ct);
        var summaryPrevTask = clickHouseRepo.GetSiteMetricsSummaryAsync(new[] { site.Id }, prevStart, prevEnd, null, ct);
        var devicesTask = clickHouseRepo.GetSiteDevicesAsync(new[] { site.Id }, start, end, null, ct);
        var locationsTask = clickHouseRepo.GetSiteLocationsAsync(new[] { site.Id }, start, end, null, ct);
        var funnelDefsTask = funnelRepo.GetFunnelsBySiteAsync(site.Id, ct);

        await Task.WhenAll(topPagesTask, hourlyActivityTask, summaryTask, summaryPrevTask, devicesTask, locationsTask, funnelDefsTask);

        // Fetch first funnel if available
        var funnelDefs = await funnelDefsTask;
        foreach (var f in funnelDefs)
        {
            var result = await funnelRepo.CalculateFunnelAsync(site.Id, f.Steps, start, end, ct);
            f.ConversionRate = result.OverallConversionRate;
            f.TotalSessions = result.Steps.FirstOrDefault()?.Sessions ?? 0;
        }

        var responseData = new PublicPreviewResponse
        {
            SiteName = site.Name,
            GeneratedAt = DateTime.UtcNow,
            Data = filledMetrics.Select(m => new PublicMetricDto
            {
                Date = m.Date,
                Visitors = m.Visitors,
                Pageviews = m.Pageviews,
                Events = m.Events,
                AvgSessionDuration = m.AvgSessionDuration
            }),
            TopPages = (await topPagesTask).Select(p => new PublicTopPageDto
            {
                Url = p.Url,
                Views = p.Views,
                AvgTimeOnPage = p.AvgTimeOnPage
            }),
            HourlyActivity = await hourlyActivityTask,
            Devices = await devicesTask,
            Summary = await summaryTask,
            Metrics30Previous = await summaryPrevTask,
            Locations = await locationsTask,
            Funnels = funnelDefs
        };

        var json = JsonSerializer.Serialize(responseData, typeof(PublicPreviewResponse), AppJsonContext.Default);
        await cache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        }, ct);

        return responseData;
    }

    private (DateTime Start, DateTime End) GetRange(DateTime? from, DateTime? to, int days)
    {
        if (from.HasValue && to.HasValue) return (from.Value, to.Value);
        
        if (days <= 0) days = 30;
        var end = DateTime.UtcNow.Date.AddDays(1);
        var start = end.AddDays(-days);
        return (start, end);
    }
}
