using System.Security.Claims;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Endpoints;

public static class SiteMetricsEndpoints
{
    public static void MapSiteMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/analytics/sites")
            .RequireAuthorization()
            .WithTags("Analytics Data");

        group.MapGet("/{siteKey}/locations", async (
            string siteKey,
            HttpRequest request,
            ClaimsPrincipal user,
            IMetricsRepository repo,
            CancellationToken ct) => {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);

            var days = int.TryParse(request.Query["days"].FirstOrDefault(), out var d) ? d : 30;
            var top = int.TryParse(request.Query["top"].FirstOrDefault(), out var t) ? t : 10;
            var data = await repo.GetSiteLocationsAsync(userId, siteKey, days, top, ct);
            return Results.Ok(data);
        })
        .RequireAuthorization()
        .WithName("GetSiteLocations");

        app.MapGet("/analytics/sites/{siteKey}/top-pages", async (
            string siteKey,
            HttpRequest request,
            ClaimsPrincipal user,
            IMetricsRepository repo,
            CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);

            var days = int.TryParse(request.Query["days"].FirstOrDefault(), out var d) ? d : 30;
            var top = int.TryParse(request.Query["top"].FirstOrDefault(), out var t) ? t : 10;
            var data = await repo.GetTopPagesAsync(userId, siteKey, days, top, ct);
            return Results.Ok(data);
        })
        .RequireAuthorization()
        .WithName("GetTopPages");

        app.MapGet("/analytics/sites/{siteKey}/custom-events/summary", async (
            string siteKey,
            HttpRequest request,
            ClaimsPrincipal user,
            IMetricsRepository repo,
            CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);

            var days = int.TryParse(request.Query["days"].FirstOrDefault(), out var d) ? d : 30;
            var data = await repo.GetCustomEventCountsAsync(userId, siteKey, days, ct);
            return Results.Ok(data);
        })
        .RequireAuthorization()
        .WithName("GetCustomEventCounts");
    }
}
