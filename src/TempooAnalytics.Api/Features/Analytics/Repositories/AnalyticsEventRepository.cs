using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Shared.Telemetry;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Repositories;

public class AnalyticsEventRepository(
    AnalyticsDbContext context, 
    IServiceProvider sp,
    ILogger<AnalyticsEventRepository>? logger = null) : IAnalyticsEventRepository
{
    private readonly ILogger<AnalyticsEventRepository> _logger = logger ?? NullLogger<AnalyticsEventRepository>.Instance;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task<SiteId> EnsureSiteAsync(string siteKey, string? ownerId, CancellationToken ct)
    {
        var existingSite = await context.AnalyticsSites
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SiteKey == siteKey, ct);

        if (existingSite is not null) return existingSite.Id;

        UserId? ownerUserId = null;
        if (!string.IsNullOrWhiteSpace(ownerId) && UserId.TryParse(ownerId, out var u))
            ownerUserId = u;

        var newSite = new AnalyticsSite
        {
            Id = SiteId.New(),
            SiteKey = siteKey,
            Name = siteKey,
            OwnerId = ownerUserId,
            CreatedAt = DateTime.UtcNow
        };

        await context.AnalyticsSites.AddAsync(newSite, ct);
        await context.SaveChangesAsync(ct);
        return newSite.Id;
    }

    public virtual async Task<Guid> InsertEventWithOutboxAsync(AnalyticsEventDto dto, CancellationToken ct = default)
    {
        using var act = TelemetryExtensions.StartActivity("repo.analytics.insertEventWithOutbox");
        act?.SetTagSafe(TelemetryConstants.TagSiteId, dto.SiteId);
        if (!string.IsNullOrWhiteSpace(dto.UserId)) act?.SetTagSafe(TelemetryConstants.TagUserId, dto.UserId);

        try
        {
            // Ensure site exists in Postgres Core
            await EnsureSiteAsync(dto.SiteId, dto.UserId, ct);
            
            var eventId = Guid.NewGuid();

            // Push to Redis Stream for ClickHouse ingestion (matching Go service or worker logic)
            var redis = sp.GetService<IConnectionMultiplexer>();
            if (redis != null)
            {
                var db = redis.GetDatabase();
                var json = JsonSerializer.Serialize(dto, AppJsonContext.Default.AnalyticsEventDto);
                await db.StreamAddAsync("stream:analytics:events", "data", json);
                
                _logger.LogInformation("Pushed analytics event {EventId} to Redis site={Site}", eventId, dto.SiteId);
            }
            else
            {
                _logger.LogWarning("Redis not available. Event {EventId} for site {Site} was NOT processed.", eventId, dto.SiteId);
            }

            act?.SetTagSafe(TelemetryConstants.TagResult, "success");
            return eventId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing analytics event for site {Site}", dto.SiteId);
            act?.SetTagSafe(TelemetryConstants.TagResult, "failure");
            throw;
        }
    }

    public async Task<bool> IsIngestAuthorizedAsync(string siteKey, string? writeKey, string? origin, CancellationToken ct = default)
    {
        using var act = TelemetryExtensions.StartActivity("repo.analytics.isIngestAuthorized");
        
        if (!string.IsNullOrWhiteSpace(writeKey))
        {
            var ok = await context.AnalyticsSites.AsNoTracking().AnyAsync(s => s.SiteKey == siteKey && s.WriteKey == writeKey, ct);
            return ok;
        }

        if (!string.IsNullOrWhiteSpace(origin))
        {
            var ok = await context.AnalyticsSites.AsNoTracking().AnyAsync(s => s.SiteKey == siteKey && s.AllowedOrigins.Contains(origin), ct);
            return ok;
        }

        return false;
    }
}
