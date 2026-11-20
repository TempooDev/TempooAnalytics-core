using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Endpoints;

public static class PublicPreviewEndpoints
{
    public static void MapPublicPreviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sites").WithTags("Public Preview");

        // Private Endpoint - Generating Token
        group.MapPost("/{siteKey}/share", GeneratePublicToken)
             .RequireAuthorization();

        // Public Endpoint - Viewing Data
        group.MapGet("/public/preview/{token}", GetPublicPreviewData)
             .AllowAnonymous();
    }

    private static async Task<IResult> GeneratePublicToken(
        string siteKey,
        ClaimsPrincipal user,
        IPublicPreviewService previewService,
        CancellationToken ct)
    {
        var userIdClaim = user.GetUserId();
        if (userIdClaim == null) return Results.Unauthorized();

        var token = await previewService.GeneratePublicTokenAsync(new UserId(userIdClaim.Value), siteKey, ct);
        if (token == null) return Results.NotFound();

        return Results.Ok(new { token });
    }

    private static async Task<IResult> GetPublicPreviewData(
        string token,
        [FromQuery] int days,
        [FromQuery] string? breakdown,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        IPublicPreviewService previewService,
        HttpContext context,
        CancellationToken ct)
    {
        var result = await previewService.GetPublicPreviewDataAsync(token, from, to, days, breakdown, ct);
        if (result == null) return Results.NotFound(new { message = "Invalid or expired token" });

        // Cache for 5 minutes (300 seconds) at the Edge/Browser level
        context.Response.Headers.CacheControl = "public, max-age=300";

        return Results.Ok(result);
    }
}
