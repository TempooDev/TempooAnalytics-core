using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Text.Json;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Imports.Models;
using TempooAnalytics.Api.Features.Notifications;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Imports.Services;

public interface IImportService
{
    /// <summary>
    /// Creates the job and hands the uploaded file to the worker. When the queue is unavailable the job is
    /// marked Failed (and the user notified) before the error is rethrown.
    /// </summary>
    Task<Guid> EnqueueCsvImport(SiteId siteId, string filePath, ImportType type, UserId createdBy);

    /// <summary>
    /// Creates a Pending GA4 job without a file yet: the API downloads the reports in the background
    /// and calls <see cref="EnqueueGa4File"/> when the file is in storage. Throws
    /// <see cref="InvalidOperationException"/> when the site already has an active import.
    /// </summary>
    Task<Guid> CreateGa4Job(SiteId siteId, string propertyId, UserId createdBy);

    /// <summary>Records the downloaded range and hands the normalised file to the worker.</summary>
    Task EnqueueGa4File(Guid jobId, SiteId siteId, string propertyId, DateOnly start, DateOnly end, string filePath);

    /// <summary>Fails a job the API was handling and notifies the user who started it.</summary>
    Task MarkFailed(Guid jobId, string errorMessage);

    /// <summary>
    /// Fails GA4 jobs whose download was lost (Pending, never handed to the worker, older than
    /// <paramref name="olderThan"/>): their Google token only lived in the memory of a process that is gone.
    /// </summary>
    Task<int> FailInterruptedGa4Jobs(TimeSpan olderThan);

    Task<bool> HasActiveImport(SiteId siteId);
    Task<List<ImportJob>> GetJobsBySite(SiteId siteId);
}

public class ImportService(
    AnalyticsDbContext dbContext,
    IImportQueue queue,
    TimeProvider timeProvider,
    IJobNotifier jobNotifier,
    ILogger<ImportService> logger) : IImportService
{
    public const string InterruptedGa4Message = "The download from Google Analytics was interrupted. Connect with Google again to retry.";
    public const string EnqueueFailedMessage = "The import could not be started. Please try again in a few minutes.";

    public async Task<Guid> EnqueueCsvImport(SiteId siteId, string filePath, ImportType type, UserId createdBy)
    {
        await EnsureNoActiveImport(siteId);

        var job = new ImportJob
        {
            SiteId = siteId,
            Type = type,
            Status = ImportStatus.Pending,
            CreatedByUserId = createdBy,
            ConfigurationJson = JsonSerializer.Serialize(new { OriginalPath = filePath })
        };

        dbContext.ImportJobs.Add(job);
        await dbContext.SaveChangesAsync();

        try
        {
            await queue.EnqueueAsync(new ImportTaskPayload(
                job.Id.ToString(),
                siteId.Value.ToString(),
                type == ImportType.Plausible ? "PLAUSIBLE" : "CSV",
                null,
                null,
                null,
                filePath
            ));
        }
        catch
        {
            // Otherwise the job would stay Pending forever and block new imports for the site.
            // A failure here must not hide the original error, which is rethrown.
            try { await MarkFailed(job.Id, EnqueueFailedMessage); }
            catch (Exception markEx) { logger.LogError(markEx, "Import job {JobId}: failed to mark it as failed after the enqueue error", job.Id); }
            throw;
        }

        return job.Id;
    }

    public async Task<Guid> CreateGa4Job(SiteId siteId, string propertyId, UserId createdBy)
    {
        await EnsureNoActiveImport(siteId);

        // Nothing about the Google connection is stored: ConfigurationJson stays null.
        var job = new ImportJob
        {
            SiteId = siteId,
            Type = ImportType.GoogleAnalytics4,
            Status = ImportStatus.Pending,
            ExternalPropertyId = propertyId,
            CreatedByUserId = createdBy,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        dbContext.ImportJobs.Add(job);
        await dbContext.SaveChangesAsync();
        return job.Id;
    }

    public async Task EnqueueGa4File(Guid jobId, SiteId siteId, string propertyId, DateOnly start, DateOnly end, string filePath)
    {
        // The range is set only once the file exists, which is how interrupted downloads are told apart.
        await dbContext.ImportJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.StartDate, start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
                .SetProperty(j => j.EndDate, end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));

        await queue.EnqueueAsync(new ImportTaskPayload(
            jobId.ToString(),
            siteId.Value.ToString(),
            "GA4",
            propertyId,
            start.ToString("yyyy-MM-dd"),
            end.ToString("yyyy-MM-dd"),
            filePath
        ));
    }

    public async Task MarkFailed(Guid jobId, string errorMessage)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.ImportJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, ImportStatus.Failed)
                .SetProperty(j => j.ErrorMessage, errorMessage)
                .SetProperty(j => j.CompletedAt, now));

        await jobNotifier.NotifyImportFinishedAsync(jobId, succeeded: false, errorMessage);
    }

    public async Task<int> FailInterruptedGa4Jobs(TimeSpan olderThan)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = now - olderThan;
        var interrupted = await dbContext.ImportJobs
            .Where(j => j.Type == ImportType.GoogleAnalytics4
                        && j.Status == ImportStatus.Pending
                        && j.StartDate == null
                        && j.CreatedAt < cutoff)
            .Select(j => j.Id)
            .ToListAsync();
        if (interrupted.Count == 0) return 0;

        // The conditions are checked again: a download may have handed its file over in between.
        var failed = await dbContext.ImportJobs
            .Where(j => interrupted.Contains(j.Id) && j.Status == ImportStatus.Pending && j.StartDate == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, ImportStatus.Failed)
                .SetProperty(j => j.ErrorMessage, InterruptedGa4Message)
                .SetProperty(j => j.CompletedAt, now));

        var failedIds = await dbContext.ImportJobs
            .Where(j => interrupted.Contains(j.Id) && j.Status == ImportStatus.Failed && j.ErrorMessage == InterruptedGa4Message)
            .Select(j => j.Id)
            .ToListAsync();
        foreach (var jobId in failedIds)
        {
            await jobNotifier.NotifyImportFinishedAsync(jobId, succeeded: false, InterruptedGa4Message);
        }
        return failed;
    }

    public Task<bool> HasActiveImport(SiteId siteId) =>
        dbContext.ImportJobs.AnyAsync(j => j.SiteId == siteId && (j.Status == ImportStatus.Pending || j.Status == ImportStatus.Processing));

    public async Task<List<ImportJob>> GetJobsBySite(SiteId siteId)
    {
        return await dbContext.ImportJobs
            .Where(j => j.SiteId == siteId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }

    private async Task EnsureNoActiveImport(SiteId siteId)
    {
        if (await HasActiveImport(siteId))
        {
            throw new InvalidOperationException("An import job is already in progress for this site.");
        }
    }
}

/// <summary>Hands import jobs to the Go worker (Redis list <c>queue:imports</c>).</summary>
public interface IImportQueue
{
    Task EnqueueAsync(ImportTaskPayload payload);
}

public class RedisImportQueue(IServiceProvider services) : IImportQueue
{
    private const string ImportQueueKey = "queue:imports";

    public async Task EnqueueAsync(ImportTaskPayload payload)
    {
        // Resolved only when enqueuing, so reading the job list does not depend on Redis being available.
        var redis = services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        await redis.ListRightPushAsync(ImportQueueKey, JsonSerializer.Serialize(payload, AppJsonContext.Default.ImportTaskPayload));
    }
}
