using System.Text.Json;
using TempooAnalytics.Api.Infrastructure.Common;
using StackExchange.Redis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TempooAnalytics.Api.Features.Notifications;

namespace TempooAnalytics.Api.Infrastructure.Services;

public class JobNotificationListener(
    IServiceScopeFactory scopeFactory,
    ILogger<JobNotificationListener> logger,
    IConnectionMultiplexer? redis = null) : BackgroundService
{
    private const string NotificationQueueKey = "queue:notifications";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (redis == null)
        {
            logger.LogWarning("Redis is not configured. Job Notification Listener will not run.");
            return;
        }

        logger.LogInformation("Job Notification Listener started.");
        var db = redis.GetDatabase();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // BLPOP (blocking pop) equivalent in StackExchange.Redis is not directly async-friendly for lists
                // We use a simple loop with a small delay or ListLeftPopAsync
                var payload = await db.ListLeftPopAsync(NotificationQueueKey);

                if (payload.IsNullOrEmpty)
                {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                await ProcessNotificationAsync(payload!, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while listening for job notifications");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Handles one message of <c>queue:notifications</c> by handing it to <see cref="IJobNotifier"/>
    /// (panel notification and, for EXPORT_COMPLETED only, the export-ready email). Unknown types are ignored.
    /// </summary>
    public async Task ProcessNotificationAsync(string json, CancellationToken ct)
    {
        try
        {
            var payload = JsonSerializer.Deserialize(json, AppJsonContext.Default.NotificationPayload);
            if (payload == null) return;

            // Logged without the payload: it can carry the email address and a signed download URL.
            logger.LogInformation("Received job notification {Type} for job {JobId}", payload.Type, payload.JobId);
            if (!Guid.TryParse(payload.JobId, out var jobId))
            {
                logger.LogWarning("Ignoring {Type} notification with an invalid job id", payload.Type);
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IJobNotifier>();

            switch (payload.Type)
            {
                case "EXPORT_COMPLETED":
                    await jobs.NotifyExportFinishedAsync(jobId, succeeded: true, downloadUrl: payload.DownloadUrl, ct: ct);
                    break;
                case "EXPORT_FAILED":
                    await jobs.NotifyExportFinishedAsync(jobId, succeeded: false, payload.ErrorMessage, ct: ct);
                    break;
                case "IMPORT_COMPLETED":
                    long? rows = long.TryParse(payload.RowsProcessed, out var parsed) ? parsed : null;
                    await jobs.NotifyImportFinishedAsync(jobId, succeeded: true, rowsProcessed: rows, ct: ct);
                    break;
                case "IMPORT_FAILED":
                    await jobs.NotifyImportFinishedAsync(jobId, succeeded: false, payload.ErrorMessage, ct: ct);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to process job notification payload");
        }
    }
}
