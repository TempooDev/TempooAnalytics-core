using System.Threading.Channels;
using Microsoft.Extensions.Options;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Features.Imports.Services;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Services;

namespace TempooAnalytics.Api.Features.Imports.Ga4;

/// <summary>A GA4 download waiting to run. Lives only in memory: the access token is never persisted.</summary>
public sealed record Ga4DownloadRequest(Guid JobId, SiteId SiteId, string PropertyId, string AccessToken);

/// <summary>Bounded in-memory queue between the import endpoint and <see cref="Ga4DownloadService"/>.</summary>
public sealed class Ga4DownloadQueue
{
    private readonly Channel<Ga4DownloadRequest> _channel = Channel.CreateBounded<Ga4DownloadRequest>(
        new BoundedChannelOptions(20) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    /// <returns>false when the queue is full.</returns>
    public bool TryQueue(Ga4DownloadRequest request) => _channel.Writer.TryWrite(request);

    public ChannelReader<Ga4DownloadRequest> Reader => _channel.Reader;
}

/// <summary>
/// Runs GA4 downloads one at a time. On start (and periodically) it fails GA4 jobs whose download was lost
/// with a previous process, since their token is gone and the user has to connect again.
/// </summary>
public sealed class Ga4DownloadService(
    Ga4DownloadQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<GoogleOAuthOptions> options,
    ILogger<Ga4DownloadService> logger) : BackgroundService
{
    /// <summary>Google access tokens last one hour, so an older download without a file can't finish.</summary>
    public static readonly TimeSpan InterruptedAfter = TimeSpan.FromHours(1);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.IsConfigured)
        {
            logger.LogWarning("Google OAuth client is not configured (Google:ClientId, Google:ClientSecret, Google:RedirectUri): Google Analytics import endpoints will answer 503");
        }

        var sweeper = SweepInterruptedJobsAsync(stoppingToken);

        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<Ga4DownloadProcessor>().ProcessAsync(request, stoppingToken);
        }

        await sweeper;
    }

    private async Task SweepInterruptedJobsAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var failed = await scope.ServiceProvider.GetRequiredService<IImportService>().FailInterruptedGa4Jobs(InterruptedAfter);
                if (failed > 0) logger.LogWarning("Marked {Count} interrupted Google Analytics import job(s) as failed", failed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to mark interrupted Google Analytics import jobs as failed");
            }
        } while (await WaitAsync(timer, ct));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}

/// <summary>Downloads one GA4 property, stores the normalised export and hands it to the worker.</summary>
public sealed class Ga4DownloadProcessor(
    IImportService imports,
    IClickHouseRepository clickHouse,
    IGoogleAnalyticsClient google,
    Ga4ReportDownloader downloader,
    IStorageService storage,
    TimeProvider timeProvider,
    ILogger<Ga4DownloadProcessor> logger)
{
    /// <summary>First day with GA4 data (Google Analytics 4 launched on 2015-08-14).</summary>
    public static readonly DateOnly EarliestDay = new(2015, 8, 14);

    /// <summary>The worker rejects larger files.</summary>
    public const long MaxFileBytes = 50L * 1024 * 1024;

    public const string NothingToImportMessage =
        "The Google Analytics property has no data before this site's first Tempoo event, so there was nothing to import.";
    public const string TooLargeMessage =
        "The Google Analytics history is too large to import in one go.";
    public const string UnexpectedErrorMessage =
        "The Google Analytics download failed unexpectedly. Connect with Google again to retry.";

    public async Task ProcessAsync(Ga4DownloadRequest request, CancellationToken ct)
    {
        // Identifiers only: the request also carries the access token, which must never be logged.
        logger.LogInformation("GA4 import {JobId}: downloading {PropertyId} for site {SiteId}", request.JobId, request.PropertyId, request.SiteId.Value);
        string? uploadedKey = null;
        try
        {
            var firstLive = await clickHouse.GetFirstLiveEventDateAsync(request.SiteId, ct);
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            var end = (firstLive ?? today).AddDays(-1);

            var created = await google.GetPropertyCreateTimeAsync(request.AccessToken, request.PropertyId, ct);
            var start = created.HasValue ? DateOnly.FromDateTime(created.Value.UtcDateTime) : EarliestDay;
            if (start < EarliestDay) start = EarliestDay;

            if (end < start)
            {
                await Fail(request, NothingToImportMessage);
                return;
            }

            await using var writer = new Ga4ExportWriter(start, end);
            await downloader.DownloadAsync(request.AccessToken, request.PropertyId, start, end, writer, ct);
            if (writer.DailyRows == 0)
            {
                await Fail(request, NothingToImportMessage);
                return;
            }

            var zipPath = Path.Combine(Path.GetTempPath(), $"tempoo-ga4-{request.JobId:N}.zip");
            try
            {
                await using (var zip = File.Create(zipPath))
                {
                    await writer.WriteZipAsync(zip, ct);
                }
                if (new FileInfo(zipPath).Length > MaxFileBytes)
                {
                    await Fail(request, TooLargeMessage);
                    return;
                }

                var key = $"imports/{request.SiteId.Value}/{request.JobId}.ga4.zip";
                await using (var zip = File.OpenRead(zipPath))
                {
                    await storage.UploadFileAsync(key, zip, "application/zip");
                }
                uploadedKey = key;

                await imports.EnqueueGa4File(request.JobId, request.SiteId, request.PropertyId, start, end, key);
                logger.LogInformation("GA4 import {JobId}: queued {DailyRows} daily and {BreakdownRows} breakdown rows ({Start} to {End})",
                    request.JobId, writer.DailyRows, writer.BreakdownRows, start, end);
            }
            finally
            {
                File.Delete(zipPath);
            }
        }
        catch (GoogleApiException ex)
        {
            logger.LogWarning("GA4 import {JobId}: Google answered {StatusCode}", request.JobId, (int?)ex.StatusCode);
            await Fail(request, ex.Message, uploadedKey);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await Fail(request, ImportService.InterruptedGa4Message, uploadedKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GA4 import {JobId}: download failed", request.JobId);
            await Fail(request, UnexpectedErrorMessage, uploadedKey);
        }
    }

    private async Task Fail(Ga4DownloadRequest request, string message, string? uploadedKey = null)
    {
        // Nothing reaches the worker, so the previously imported history is kept.
        if (uploadedKey != null)
        {
            try { await storage.DeleteFileAsync(uploadedKey); }
            catch (Exception ex) { logger.LogWarning(ex, "GA4 import {JobId}: failed to delete the uploaded file", request.JobId); }
        }
        await imports.MarkFailed(request.JobId, message);
    }
}
