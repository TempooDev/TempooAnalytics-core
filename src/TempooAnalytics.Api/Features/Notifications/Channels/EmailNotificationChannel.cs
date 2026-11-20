using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Notifications.Models;
using TempooAnalytics.Api.Infrastructure.Email;

namespace TempooAnalytics.Api.Features.Notifications.Channels;

/// <summary>
/// Email to the user's address, with a template per notification type. Types without a template are skipped.
/// Not behind the Notifications flag: the export-ready email predates the inbox.
/// </summary>
public sealed class EmailNotificationChannel(
    AnalyticsDbContext db,
    IEmailService email,
    ILogger<EmailNotificationChannel> logger) : INotificationChannel
{
    public NotificationChannels Channel => NotificationChannels.Email;

    public static bool HasTemplate(string type) => type == NotificationTypes.ExportCompleted;

    public async Task SendAsync(UserNotification n, CancellationToken ct = default)
    {
        if (!HasTemplate(n.Type))
        {
            logger.LogWarning("No email template for {Type} notifications", n.Type);
            return;
        }

        try
        {
            var to = await db.Users.AsNoTracking().Where(u => u.Id == n.UserId).Select(u => u.Email).FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(to))
            {
                logger.LogWarning("User {UserId} has no email: {Type} email not sent", n.UserId.Value, n.Type);
                return;
            }

            switch (n.Type)
            {
                case NotificationTypes.ExportCompleted:
                    if (n.Data?.GetValueOrDefault(NotificationData.DownloadUrl) is not { Length: > 0 } downloadUrl)
                    {
                        logger.LogWarning("Export-ready email for job {SourceId} has no download URL", n.SourceId);
                        return;
                    }
                    logger.LogInformation("Sending export ready email for job {SourceId}", n.SourceId);
                    await email.SendExportReadyEmailAsync(to, downloadUrl);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to send {Type} email to user {UserId}", n.Type, n.UserId.Value);
        }
    }
}
