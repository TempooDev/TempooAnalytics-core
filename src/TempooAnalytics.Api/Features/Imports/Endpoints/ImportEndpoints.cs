using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Features.Imports.Dtos;
using TempooAnalytics.Api.Features.Imports.Ga4;
using TempooAnalytics.Api.Features.Imports.Models;
using TempooAnalytics.Api.Features.Imports.Services;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.FeatureFlags;
using TempooAnalytics.Api.Infrastructure.Services;

namespace TempooAnalytics.Api.Features.Imports.Endpoints;

public static class ImportEndpoints
{
    private const long MaxUploadBytes = 50 * 1024 * 1024;

    public static void MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sites/{siteId}/imports")
            .WithTags("Imports")
            .RequireAuthorization();

        group.MapGa4ImportEndpoints();

        group.MapPost("/plausible", async (
            Guid siteId,
            IFormFile file,
            ClaimsPrincipal user,
            HttpContext http,
            [FromServices] IFeatureFlags featureFlags,
            [FromServices] IImportService importService,
            [FromServices] IStorageAvailability storage,
            CancellationToken ct) =>
        {
            // Not released yet: behave as if the endpoint didn't exist.
            if (!featureFlags.PlausibleImport) return Results.NotFound();

            // No object storage on this server: say so, before anything is read or a job is created.
            if (!storage.IsAvailable) return StorageUnavailable();

            // Checked before anything is uploaded.
            var userId = user.GetRequiredUserId();
            if (await CheckSiteAndPlanAsync(http, userId, siteId, ct) is { } denied) return denied;

            if (file.Length == 0) return Results.BadRequest("File is empty");
            if (file.Length > MaxUploadBytes) return Results.BadRequest("File is too large (max 50 MB)");
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension is not (".zip" or ".csv"))
                return Results.BadRequest("Only Plausible export .zip or .csv files are supported");

            // Resolved only now: the storage service refuses to be built without credentials, which must not turn into a 500 above.
            var storageService = http.RequestServices.GetRequiredService<IStorageService>();

            // The object key never uses the client-supplied file name.
            var r2Key = $"imports/{siteId}/{Guid.NewGuid()}{extension}";
            var contentType = extension == ".zip" ? "application/zip" : "text/csv";
            using (var stream = file.OpenReadStream())
            {
                await storageService.UploadFileAsync(r2Key, stream, contentType);
            }

            try
            {
                var jobId = await importService.EnqueueCsvImport(new SiteId(siteId), r2Key, ImportType.Plausible, new UserId(userId));
                return Results.Accepted($"/api/sites/{siteId}/imports/{jobId}", new JobIdResponse(jobId));
            }
            catch (InvalidOperationException ex)
            {
                // Another import is still running for this site; don't leave the upload orphaned.
                await storageService.DeleteFileAsync(r2Key);
                return Results.Conflict(ex.Message);
            }
        })
        // Authenticated with a bearer token, not cookies, so CSRF tokens don't apply to this upload.
        .DisableAntiforgery();

        group.MapGet("/", async (
            Guid siteId,
            ClaimsPrincipal user,
            [FromServices] ISiteAccessService siteAccess,
            [FromServices] IImportService importService,
            CancellationToken ct) =>
        {
            if (!await siteAccess.CanAccessAsync(new UserId(user.GetRequiredUserId()), new SiteId(siteId), ct))
                return Results.Forbid();

            var jobs = await importService.GetJobsBySite(new SiteId(siteId));
            var dtos = jobs.Select(j => new ImportJobDto(
                j.Id,
                j.SiteId.Value,
                j.Type,
                j.Status,
                j.CreatedAt,
                j.RowsProcessed,
                j.ErrorMessage,
                j.StartedAt,
                j.CompletedAt));

            return Results.Ok(dtos);
        });
    }

    /// <summary>501: the feature needs object storage (S3/R2) and this server has none configured.</summary>
    public static IResult StorageUnavailable() => Results.Json(
        new ErrorMessagePayload(StorageUnavailableMessage),
        AppJsonContext.Default.ErrorMessagePayload,
        statusCode: StatusCodes.Status501NotImplemented);

    public const string StorageUnavailableMessage =
        "Importing and exporting data is not available on this server: it has no S3-compatible object storage configured (Storage:R2:*).";

    /// <summary>
    /// Imports need access to the site and, like exports, a paid plan. Returns the 403 to send, or null.
    /// </summary>
    public static async Task<IResult?> CheckSiteAndPlanAsync(HttpContext http, Guid userId, Guid siteId, CancellationToken ct)
    {
        var services = http.RequestServices;
        var user = new UserId(userId);
        if (!await services.GetRequiredService<ISiteAccessService>().CanAccessAsync(user, new SiteId(siteId), ct))
            return Results.Forbid();

        var plan = await services.GetRequiredService<IUserPlanStore>().GetPlanAsync(user);
        if (plan == null || plan.Slug == "free")
        {
            return Results.Json(
                new ErrorMessagePayload("Importing history is available on paid plans. Please upgrade your plan."),
                AppJsonContext.Default.ErrorMessagePayload,
                statusCode: StatusCodes.Status403Forbidden);
        }
        return null;
    }
}

public record JobIdResponse(Guid JobId);
