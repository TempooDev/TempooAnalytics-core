using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Notifications.Models;
using TempooAnalytics.Api.Infrastructure.FeatureFlags;

namespace TempooAnalytics.Api.Features.Notifications.Channels;

/// <summary>
/// The panel inbox (table notifications.notifications). Does nothing while the Notifications flag is off or
/// when (UserId, Type, SourceId) already exists.
/// </summary>
public sealed class InAppNotificationChannel(
    AnalyticsDbContext db,
    IFeatureFlags featureFlags,
    TimeProvider timeProvider,
    ILogger<InAppNotificationChannel> logger) : INotificationChannel
{
    public const int MaxTitleLength = 200;
    public const int MaxBodyLength = 1000;
    public const int MaxLinkLength = 500;

    public NotificationChannels Channel => NotificationChannels.InApp;

    public async Task SendAsync(UserNotification n, CancellationToken ct = default)
    {
        if (!featureFlags.Notifications) return;

        try
        {
            var id = Guid.NewGuid();
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var title = Truncate(n.Title, MaxTitleLength)!;
            var body = Truncate(n.Body, MaxBodyLength);
            var link = Truncate(n.Link, MaxLinkLength);
            var severity = (short)n.Severity;
            var siteId = n.SiteId?.Value;

            if (!db.Database.IsRelational())
            {
                // EF InMemory (unit tests) has no ON CONFLICT: same outcome, without the race protection.
                if (n.SourceId != null && await db.Notifications.AnyAsync(x => x.UserId == n.UserId && x.Type == n.Type && x.SourceId == n.SourceId, ct))
                    return;
                db.Notifications.Add(new Notification
                {
                    Id = id, UserId = n.UserId, Type = n.Type, Severity = n.Severity, Title = title, Body = body,
                    Link = link, SiteId = n.SiteId, SourceId = n.SourceId, CreatedAt = now
                });
                await db.SaveChangesAsync(ct);
                return;
            }

            // Two copies of the same queue message can arrive at once: the partial unique index decides.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO notifications.notifications (id, user_id, type, severity, title, body, link, site_id, source_id, created_at)
                VALUES ({id}, {n.UserId.Value}, {n.Type}, {severity}, {title}, {body}, {link}, {siteId}, {n.SourceId}, {now})
                ON CONFLICT (user_id, type, source_id) WHERE source_id IS NOT NULL DO NOTHING
                """, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to store {Type} notification for user {UserId} (source {SourceId})", n.Type, n.UserId.Value, n.SourceId);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value == null || value.Length <= max ? value : value[..(max - 1)] + "…";
}
