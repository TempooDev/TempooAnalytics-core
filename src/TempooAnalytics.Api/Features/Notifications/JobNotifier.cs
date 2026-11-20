using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Imports.Models;
using TempooAnalytics.Api.Features.Notifications.Models;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Notifications;

/// <summary>
/// Turns the end of an import or export job into a notification for the user who started it.
/// Used by the API when it fails a job itself and by the listener of <c>queue:notifications</c>.
/// </summary>
public interface IJobNotifier
{
    /// <param name="rowsProcessed">Rows reported by the worker; the job's own count when null.</param>
    Task NotifyImportFinishedAsync(Guid jobId, bool succeeded, string? errorMessage = null, long? rowsProcessed = null, CancellationToken ct = default);

    /// <param name="downloadUrl">When the export is ready, also sends the export-ready email with this link.</param>
    /// <param name="errorMessage">Only logged: the user gets a generic text.</param>
    Task NotifyExportFinishedAsync(Guid jobId, bool succeeded, string? errorMessage = null, string? downloadUrl = null, CancellationToken ct = default);
}

public sealed class JobNotifier(
    AnalyticsDbContext db,
    INotificationDispatcher notifications,
    ILogger<JobNotifier> logger) : IJobNotifier
{
    public async Task NotifyImportFinishedAsync(Guid jobId, bool succeeded, string? errorMessage = null, long? rowsProcessed = null, CancellationToken ct = default)
    {
        try
        {
            var job = await db.ImportJobs.AsNoTracking()
                .Where(j => j.Id == jobId)
                .Select(j => new { j.SiteId, j.Type, j.CreatedByUserId, j.RowsProcessed, j.ErrorMessage })
                .FirstOrDefaultAsync(ct);
            if (job == null)
            {
                logger.LogWarning("Import job {JobId} not found: no notification", jobId);
                return;
            }

            var site = await db.AnalyticsSites.AsNoTracking()
                .Where(s => s.Id == job.SiteId)
                .Select(s => new { s.Name, s.OwnerId })
                .FirstOrDefaultAsync(ct);
            if (site == null) return;

            var recipient = ImportRecipient(job.CreatedByUserId, site.OwnerId);
            if (recipient == null)
            {
                logger.LogWarning("Import job {JobId} has no user and its site no owner: no notification", jobId);
                return;
            }

            var text = succeeded
                ? NotificationTexts.ImportCompleted(site.Name, job.Type, rowsProcessed ?? job.RowsProcessed)
                : NotificationTexts.ImportFailed(site.Name, job.Type, errorMessage ?? job.ErrorMessage);

            await notifications.DispatchAsync(new UserNotification(
                recipient.Value,
                succeeded ? NotificationTypes.ImportCompleted : NotificationTypes.ImportFailed,
                succeeded ? NotificationSeverity.Success : NotificationSeverity.Error,
                text.Title,
                text.Body,
                NotificationTexts.ImportsLink(job.SiteId),
                job.SiteId,
                jobId), ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to notify the end of import job {JobId}", jobId);
        }
    }

    public async Task NotifyExportFinishedAsync(Guid jobId, bool succeeded, string? errorMessage = null, string? downloadUrl = null, CancellationToken ct = default)
    {
        try
        {
            var job = await db.ExportJobs.AsNoTracking()
                .Where(j => j.Id == jobId)
                .Select(j => new { j.SiteId, j.UserId, j.ErrorMessage })
                .FirstOrDefaultAsync(ct);
            if (job == null)
            {
                logger.LogWarning("Export job {JobId} not found: no notification", jobId);
                return;
            }

            // Also for a site deleted meanwhile: the download link of a ready export only arrives by email.
            var siteName = await db.AnalyticsSites.AsNoTracking()
                .IgnoreQueryFilters()
                .Where(s => s.Id == job.SiteId)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(ct) ?? "your site";

            if (!succeeded)
                logger.LogInformation("Export job {JobId} failed: {Error}", jobId, errorMessage ?? job.ErrorMessage);
            var text = succeeded
                ? NotificationTexts.ExportCompleted(siteName)
                : NotificationTexts.ExportFailed(siteName);

            // The download link only goes by email: it expires, so the panel links to the dashboard.
            var sendEmail = succeeded && !string.IsNullOrEmpty(downloadUrl);
            await notifications.DispatchAsync(new UserNotification(
                job.UserId,
                succeeded ? NotificationTypes.ExportCompleted : NotificationTypes.ExportFailed,
                succeeded ? NotificationSeverity.Success : NotificationSeverity.Error,
                text.Title,
                text.Body,
                NotificationTexts.DashboardLink,
                job.SiteId,
                jobId,
                sendEmail ? new Dictionary<string, string> { [NotificationData.DownloadUrl] = downloadUrl! } : null),
                sendEmail ? NotificationChannels.InApp | NotificationChannels.Email : NotificationChannels.InApp,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to notify the end of export job {JobId}", jobId);
        }
    }

    /// <summary>The user who started the import; the site owner for jobs created before that was stored.</summary>
    public static UserId? ImportRecipient(UserId? createdBy, UserId? siteOwner) => createdBy ?? siteOwner;
}
