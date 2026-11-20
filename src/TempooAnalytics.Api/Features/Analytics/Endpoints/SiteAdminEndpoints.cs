using System.Security.Claims;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Endpoints;

public static class SiteAdminEndpoints
{
    public static void MapSiteAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // Create new site. Any signed-in user creates sites they own; the plan's site limit is checked in SiteService.
        // Not in an AdminPolicy group: a group policy adds to the endpoint's own, so normal users would get 403.
        app.MapPost("/admin/sites", async (
            CreateSiteRequest request,
            ISiteService siteService,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var ownerIdClaim = user.GetUserId();
            if (ownerIdClaim == null) return Results.Unauthorized();
            var ownerId = new UserId(ownerIdClaim.Value);

            var result = await siteService.CreateSiteAsync(ownerId, request, ct);
            
            if (!result.IsSuccess)
            {
                return result.ErrorType switch
                {
                    ResultErrorType.Conflict => Results.Conflict(new { message = result.ErrorMessage }),
                    _ => Results.BadRequest(new { message = result.ErrorMessage })
                };
            }

            return Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags("Admin: Sites")
        .WithName("CreateSite");

        app.MapPost("/admin/sites/{siteKey}/write-key", async (
            string siteKey,
            HttpRequest request,
            ISiteService siteService,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            if (!IsAuthorizedAdmin(request, configuration))
                return Results.StatusCode(StatusCodes.Status401Unauthorized);

            var adminUser = GetAdminUser(request);
            var result = await siteService.AdminGenerateWriteKeyAsync(siteKey, adminUser, ct);

            return Results.Ok(result);
        })
        .WithName("AdminGenerateSiteWriteKey");

        // Soft Delete Site
        app.MapDelete("/admin/sites/{siteKey}", async (
            string siteKey,
            ISiteService siteService,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var ownerIdClaim = user.GetUserId();
            if (ownerIdClaim == null) return Results.Unauthorized();
            var ownerId = new UserId(ownerIdClaim.Value);

            var success = await siteService.DeleteSiteAsync(ownerId, siteKey, ct);
            if (!success) return Results.NotFound();

            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithName("DeleteSite");
    }

    private static bool IsAuthorizedAdmin(HttpRequest request, IConfiguration configuration)
    {
        var adminKey = configuration["Management:ApiKey"];
        if (string.IsNullOrWhiteSpace(adminKey)) return false;

        string? provided = null;
        if (request.Headers.TryGetValue("Authorization", out var auth) && auth.Count > 0)
        {
            var a = auth[0];
            if (a!.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                provided = a.Substring("Bearer ".Length).Trim();
        }
        if (string.IsNullOrWhiteSpace(provided) && request.Headers.TryGetValue("X-Admin-Key", out var adminHeader))
        {
            provided = adminHeader.FirstOrDefault();
        }

        return provided == adminKey;
    }

    private static string GetAdminUser(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Admin-User", out var adminUserHeader) && !string.IsNullOrWhiteSpace(adminUserHeader.FirstOrDefault()))
        {
            return adminUserHeader.First()!;
        }
        return "system";
    }
}
