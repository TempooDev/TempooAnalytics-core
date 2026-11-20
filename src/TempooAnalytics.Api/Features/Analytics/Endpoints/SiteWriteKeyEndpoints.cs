using System.Security.Claims;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Shared.Telemetry;

namespace TempooAnalytics.Api.Features.Analytics.Endpoints;

public static class SiteWriteKeyEndpoints
{
    public static void MapSiteWriteKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sites")
            .RequireAuthorization()
            .WithTags("Site Management");

        group.MapPost("/{siteKey}/write-key", async (string siteKey, ClaimsPrincipal user, ISiteService siteService, ILoggerFactory loggerFactory, HttpContext ctx, CancellationToken ct) => {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);

            var result = await siteService.GenerateWriteKeyAsync(userId, siteKey, ct);
            if (!result.IsSuccess)
            {
                return result.ErrorType switch
                {
                    ResultErrorType.Unauthorized => Results.Forbid(),
                    _ => Results.BadRequest(new { message = result.ErrorMessage })
                };
            }

            var data = result.Value;
            var logger = loggerFactory.CreateLogger("SiteWriteKeyEndpoints");
            var masked = TelemetryExtensions.MaskSecret(data.writeKey);
            logger.LogSiteKeyAction("generated", data.siteKey, userId.Value, masked, ctx);

            return Results.Ok(new { siteKey = data.siteKey, writeKey = data.writeKey });
        });

        group.MapPost("/{siteKey}/write-key/rotate", async (string siteKey, ClaimsPrincipal user, ISiteService siteService, ILoggerFactory loggerFactory, HttpContext ctx, CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);

            var rotated = await siteService.RotateWriteKeyAsync(userId, siteKey, ct);
            if (rotated is null) return Results.StatusCode(StatusCodes.Status403Forbidden);

            var logger = loggerFactory.CreateLogger("SiteWriteKeyEndpoints");
            var masked = TelemetryExtensions.MaskSecret(rotated.Value.writeKey);
            logger.LogSiteKeyAction("rotated", rotated.Value.siteKey, userId.Value, masked, ctx);

            return Results.Ok(new { siteKey = rotated.Value.siteKey, writeKey = rotated.Value.writeKey });
        });

        group.MapDelete("/{siteKey}/write-key", async (string siteKey, ClaimsPrincipal user, ISiteService siteService, ILoggerFactory loggerFactory, HttpContext ctx, CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);

            var revoked = await siteService.RevokeWriteKeyAsync(userId, siteKey, ct);
            if (revoked is null) return Results.StatusCode(StatusCodes.Status403Forbidden);

            var logger = loggerFactory.CreateLogger("SiteWriteKeyEndpoints");
            logger.LogSiteKeyAction("revoked", revoked, userId.Value, null, ctx);

            return Results.NoContent();
        });
    }
}
