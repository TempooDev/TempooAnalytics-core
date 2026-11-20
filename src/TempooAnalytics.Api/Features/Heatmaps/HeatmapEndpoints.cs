using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.FeatureFlags;

namespace TempooAnalytics.Api.Features.Heatmaps;

public static class HeatmapEndpoints
{
    public static void MapHeatmapEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/analytics/heatmaps").RequireAuthorization().WithTags("Heatmaps")
            .AddEndpointFilter(NotFoundUnlessReleased);

        group.MapGet("/pages", async Task<IResult> ([FromQuery] string siteId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? device,
            IHeatmapService heatmaps, ClaimsPrincipal user, TimeProvider time, CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            if (!TryDevice(device, out var dev)) return Results.BadRequest(new { message = "device must be desktop, tablet or mobile" });

            var (start, end) = HeatmapGrid.Range(from, to, time.GetUtcNow().UtcDateTime);
            var result = await heatmaps.GetPagesAsync(new UserId(userIdClaim.Value), siteId, start, end, dev, ct);
            return ToResult(result);
        }).WithName("GetHeatmapPages");

        group.MapGet("/", async Task<IResult> ([FromQuery] string siteId, [FromQuery] string path, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? device,
            IHeatmapService heatmaps, ClaimsPrincipal user, TimeProvider time, CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            if (!TryDevice(device, out var dev)) return Results.BadRequest(new { message = "device must be desktop, tablet or mobile" });
            if (string.IsNullOrWhiteSpace(path)) return Results.BadRequest(new { message = "path is required" });

            var (start, end) = HeatmapGrid.Range(from, to, time.GetUtcNow().UtcDateTime);
            var result = await heatmaps.GetHeatmapAsync(new UserId(userIdClaim.Value), siteId, path, start, end, dev, ct);
            return ToResult(result);
        }).WithName("GetHeatmap");

        group.MapPost("/overlay-token", async Task<IResult> ([FromBody] HeatmapOverlayTokenRequest request, IHeatmapService heatmaps, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();

            var result = await heatmaps.IssueOverlayTokenAsync(new UserId(userIdClaim.Value), request, ct);
            return ToResult(result);
        }).WithName("CreateHeatmapOverlayToken");

        // Overlay mode: read by heatmap-overlay.js on the customer's own site, without a session. The token
        // grants one page of one site for 10 minutes, and only the site's origins may read the response.
        app.MapGet("/public/heatmaps/overlay", async Task<IResult> ([FromQuery] string? token, [FromQuery] string? siteId, [FromQuery] string? device,
            IHeatmapOverlayTokenService tokens, IHeatmapService heatmaps, ISiteRepository sites, HttpContext context, TimeProvider time, CancellationToken ct) =>
        {
            context.Response.Headers.Vary = "Origin";
            var grant = tokens.Read(token ?? "");
            if (grant is null) return Results.Unauthorized();

            var site = await sites.GetSiteByIdAsync(grant.SiteId, ct);
            if (site is null) return Results.Unauthorized();
            // The overlay sends the data-site-id of the tracker on the page: a token for another site is refused.
            if (!HeatmapOverlayOrigins.IsSameSite(siteId, site.Id.Value, site.SiteKey)) return Results.Unauthorized();

            var origin = context.Request.Headers.Origin.ToString();
            if (HeatmapOverlayOrigins.IsAllowed(origin, site.AllowedOrigins, site.Name))
                context.Response.Headers.AccessControlAllowOrigin = origin;

            if (!TryDevice(device, out var dev)) return Results.BadRequest(new { message = "device must be desktop, tablet or mobile" });

            var (start, end) = HeatmapGrid.Range(null, null, time.GetUtcNow().UtcDateTime);
            context.Response.Headers.CacheControl = "private, no-store";
            return Results.Ok(await heatmaps.GetHeatmapForSiteAsync(grant.SiteId, grant.Path, start, end, dev, ct));
        })
        .AllowAnonymous()
        .AddEndpointFilter(NotFoundUnlessReleased)
        .WithTags("Heatmaps")
        .WithName("GetHeatmapOverlay");
    }

    // Not released yet (FeatureFlags:Heatmaps): behave as if the endpoints didn't exist.
    private static async ValueTask<object?> NotFoundUnlessReleased(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.RequestServices.GetRequiredService<IFeatureFlags>().Heatmaps
            ? await next(context)
            : Results.NotFound();

    private static bool TryDevice(string? device, out string value)
    {
        value = string.IsNullOrWhiteSpace(device) ? HeatmapDevices.Desktop : device.Trim().ToLowerInvariant();
        return HeatmapDevices.All.Contains(value);
    }

    private static IResult ToResult<T>(Result<T> result)
    {
        if (result.IsSuccess) return Results.Ok(result.Value);
        return result.ErrorType switch
        {
            ResultErrorType.Forbidden => Results.Forbid(),
            ResultErrorType.Validation => Results.BadRequest(new { message = result.ErrorMessage }),
            _ => Results.NotFound(new { message = result.ErrorMessage })
        };
    }
}
