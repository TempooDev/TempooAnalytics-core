using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Email;
using TempooAnalytics.Api.Infrastructure.Services;
using TempooAnalytics.Api.Features.Notifications;

namespace TempooAnalytics.Api.Features.Analytics.Endpoints;

public record ExportDataRequest(DateTime From, DateTime To);

public static class ExportEndpoints
{
    public static void MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/sites/{siteId}/export", async (
            Guid siteId,
            ExportDataRequest request,
            ClaimsPrincipal user,
            AnalyticsDbContext dbContext,
            IUserRepository userRepository,
            IUserPlanStore planStore,
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory,
            ISiteAccessService siteAccess,
            IStorageAvailability storage) =>
        {
            // The CSV is written to object storage by the worker: without it the job would stay pending for ever.
            if (!storage.IsAvailable) return Imports.Endpoints.ImportEndpoints.StorageUnavailable();

            var userId = user.GetRequiredUserId();
            // The export is emailed to the requester, so they must have access to the site.
            if (!await siteAccess.CanAccessAsync(new UserId(userId), new SiteId(siteId)))
                return Results.Forbid();

            var plan = await planStore.GetPlanAsync(new UserId(userId));
            
            // Validate the paid tier (slug "free" is the default free one)
            if (plan == null || plan.Slug == "free")
            {
                return Results.Json(new ErrorMessagePayload("Exporting data is a premium feature. Please upgrade your plan."), AppJsonContext.Default.ErrorMessagePayload, statusCode: 403);
            }

            // Concurrency check
            var activeExport = await dbContext.ExportJobs
                .AnyAsync(j => j.SiteId == new SiteId(siteId) && (j.Status == ExportStatus.Pending || j.Status == ExportStatus.Processing));

            if (activeExport)
            {
                return Results.Json(new ErrorMessagePayload("An export process is already running for this site. Please wait until it finishes."), AppJsonContext.Default.ErrorMessagePayload, statusCode: 400);
            }

            var dbUser = await userRepository.FindByIdAsync(new UserId(userId));
            if (dbUser == null) return Results.Unauthorized();

            // Register the job
            var job = new ExportJob
            {
                SiteId = new SiteId(siteId),
                UserId = new UserId(userId),
                Status = ExportStatus.Pending,
                DateFrom = request.From,
                DateTo = request.To
            };
            dbContext.ExportJobs.Add(job);
            await dbContext.SaveChangesAsync();

            var logger = loggerFactory.CreateLogger("ExportEndpoints");

            // Background execution (DELEGATED TO GO)
            _ = Task.Run(async () =>
            {
                try
                {
                    logger.LogInformation("Enqueuing export job {JobId} for Go worker", job.Id);
                    
                    var payloadObj = new ExportTaskPayload(
                        "EXPORT",
                        job.Id.ToString(),
                        siteId.ToString(),
                        request.From.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        request.To.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        dbUser.Email
                    );

                    var payload = System.Text.Json.JsonSerializer.Serialize(payloadObj, AppJsonContext.Default.ExportTaskPayload);

                    // Not serviceProvider: that is the request's scope, already disposed if the request finished first.
                    using var redisScope = scopeFactory.CreateScope();
                    var redis = redisScope.ServiceProvider.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>();
                    await redis.GetDatabase().ListRightPushAsync("queue:imports", payload);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to enqueue export job {JobId}", job.Id);
                    // The request may be over by now: use a scope of the application, not of the request.
                    using var scope = scopeFactory.CreateScope();
                    var innerDb = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
                    var currentJob = await innerDb.ExportJobs.FindAsync(job.Id);
                    if (currentJob != null)
                    {
                        currentJob.Status = ExportStatus.Failed;
                        currentJob.ErrorMessage = "Failed to queue the export task.";
                        await innerDb.SaveChangesAsync();
                        await scope.ServiceProvider.GetRequiredService<IJobNotifier>()
                            .NotifyExportFinishedAsync(job.Id, succeeded: false, currentJob.ErrorMessage);
                    }
                }
            });

            return Results.Json(new SuccessMessagePayload("Export request received. We are processing your data and you will receive an email shortly."), AppJsonContext.Default.SuccessMessagePayload);
        })
        .RequireAuthorization();
        
        app.MapGet("/sites/{siteId}/export/status", async (
            Guid siteId,
            ClaimsPrincipal user,
            AnalyticsDbContext dbContext,
            ISiteAccessService siteAccess) =>
        {
            if (!await siteAccess.CanAccessAsync(new UserId(user.GetRequiredUserId()), new SiteId(siteId)))
                return Results.Forbid();

            var activeJob = await dbContext.ExportJobs
                .Where(j => j.SiteId == new SiteId(siteId) && (j.Status == ExportStatus.Pending || j.Status == ExportStatus.Processing))
                .OrderByDescending(j => j.CreatedAt)
                .Select(j => new { j.Id, j.Status, j.CreatedAt })
                .FirstOrDefaultAsync();

            var jobDto = activeJob != null ? new ExportJobStatusDto(activeJob.Id, activeJob.Status, activeJob.CreatedAt) : null;
            return Results.Ok(new ExportStatusResponse(activeJob != null, jobDto));
        })
        .WithName("GetExportStatus")
        .WithTags("Analytics")
        .RequireAuthorization();
    }
}

public record ExportJobStatusDto(Guid Id, ExportStatus Status, DateTime CreatedAt);
public record ExportStatusResponse(bool IsRunning, ExportJobStatusDto? Job);
