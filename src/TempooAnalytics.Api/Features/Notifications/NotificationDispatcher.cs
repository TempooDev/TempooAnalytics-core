using TempooAnalytics.Api.Features.Notifications.Models;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Notifications;

/// <summary>Where a notification is delivered.</summary>
[Flags]
public enum NotificationChannels
{
    InApp = 1,
    Email = 2
}

/// <summary>
/// Something that happened for a user. Each channel renders it its own way: the panel stores the title and
/// text, the email channel picks the template of <see cref="Type"/>.
/// </summary>
/// <param name="Data">Extra values a channel needs (e.g. <see cref="NotificationData.DownloadUrl"/>), never shown in the panel.</param>
public sealed record UserNotification(
    UserId UserId,
    string Type,
    NotificationSeverity Severity,
    string Title,
    string? Body = null,
    string? Link = null,
    SiteId? SiteId = null,
    Guid? SourceId = null,
    IReadOnlyDictionary<string, string>? Data = null);

public static class NotificationData
{
    /// <summary>Signed export URL: only sent by email, it expires after 24 hours.</summary>
    public const string DownloadUrl = "downloadUrl";
}

/// <summary>A way to deliver notifications. Implementations must not throw: a failed delivery is logged.</summary>
public interface INotificationChannel
{
    NotificationChannels Channel { get; }
    Task SendAsync(UserNotification notification, CancellationToken ct = default);
}

/// <summary>The only way to notify a user. Features never write to a channel directly.</summary>
public interface INotificationDispatcher
{
    Task DispatchAsync(UserNotification notification, NotificationChannels channels = NotificationChannels.InApp, CancellationToken ct = default);
}

public sealed class NotificationDispatcher(
    IEnumerable<INotificationChannel> channels,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    public async Task DispatchAsync(UserNotification notification, NotificationChannels requested = NotificationChannels.InApp, CancellationToken ct = default)
    {
        foreach (var channel in channels.Where(c => requested.HasFlag(c.Channel)))
        {
            // One channel failing never stops the others (nor the process that triggered the notification).
            try
            {
                await channel.SendAsync(notification, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Channel {Channel} failed to deliver {Type} notification for user {UserId}", channel.Channel, notification.Type, notification.UserId.Value);
            }
        }
    }
}
