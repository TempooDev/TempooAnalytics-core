using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Analytics.Dtos.Funnels;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Shared.Telemetry;

namespace TempooAnalytics.Api.Features.Analytics.Endpoints;

public static class FunnelEndpoints
{
    public static void MapFunnelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/analytics/funnels").RequireAuthorization().WithTags("Funnels");

        group.MapGet("/event-metadata", async Task<IResult> ([FromQuery] string siteId, [FromQuery] int? days, [FromQuery] DateTime? from, [FromQuery] DateTime? to, IFunnelService funnelService, ClaimsPrincipal user, IPlanPolicy planPolicy, CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);
            
            if (!await planPolicy.HasAdvancedAnalyticsAsync(userId, ct)) return Results.Forbid();

            var (start, end) = GetRange(from, to, days ?? 7);
            var result = await funnelService.GetEventMetadataAsync(userId, siteId, start, end, ct);
            
            if (!result.IsSuccess)
                return result.ErrorType == ResultErrorType.Forbidden ? Results.Forbid() : Results.NotFound(result.ErrorMessage);
                
            return Results.Ok(result.Value);
        }).WithName("GetEventMetadata");

        group.MapGet("/", async Task<IResult> ([FromQuery] string siteId, IFunnelService funnelService, ClaimsPrincipal user, IPlanPolicy planPolicy, CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);
            
            if (!await planPolicy.HasAdvancedAnalyticsAsync(userId, ct)) return Results.Forbid();

            var result = await funnelService.GetFunnelsBySiteAsync(userId, siteId, ct);
            
            if (!result.IsSuccess)
                return result.ErrorType == ResultErrorType.Forbidden ? Results.Forbid() : Results.NotFound(result.ErrorMessage);

            return Results.Ok(result.Value);
        }).WithName("GetFunnels");

        group.MapPost("/", async Task<IResult> ([FromBody] FunnelDefinitionDto dto, IFunnelService funnelService, ClaimsPrincipal user, IPlanPolicy planPolicy, CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);
            
            if (!await planPolicy.HasAdvancedAnalyticsAsync(userId, ct)) return Results.Forbid();

            var result = await funnelService.CreateFunnelAsync(userId, dto, ct);
            
            if (!result.IsSuccess)
                return result.ErrorType == ResultErrorType.Forbidden ? Results.Forbid() : Results.BadRequest(result.ErrorMessage);

            return Results.Ok(result.Value);
        }).WithName("CreateFunnel");

        group.MapPut("/{id:guid}", async Task<IResult> (Guid id, [FromBody] FunnelDefinitionDto dto, IFunnelService funnelService, ClaimsPrincipal user, IPlanPolicy planPolicy, CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);
            
            if (!await planPolicy.HasAdvancedAnalyticsAsync(userId, ct)) return Results.Forbid();

            var result = await funnelService.UpdateFunnelAsync(userId, id, dto, ct);
            if (!result.IsSuccess)
            {
                if (result.ErrorType == ResultErrorType.Forbidden) return Results.Forbid();
                return result.ErrorType == ResultErrorType.NotFound ? Results.NotFound() : Results.BadRequest(result.ErrorMessage);
            }
            return Results.NoContent();
        }).WithName("UpdateFunnel");

        group.MapDelete("/{id:guid}", async Task<IResult> (Guid id, [FromQuery] string siteId, IFunnelService funnelService, ClaimsPrincipal user, IPlanPolicy planPolicy, CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);
            
            if (!await planPolicy.HasAdvancedAnalyticsAsync(userId, ct)) return Results.Forbid();

            var result = await funnelService.DeleteFunnelAsync(userId, id, siteId, ct);
            if (!result.IsSuccess)
            {
                if (result.ErrorType == ResultErrorType.Forbidden) return Results.Forbid();
                return result.ErrorType == ResultErrorType.NotFound ? Results.NotFound() : Results.BadRequest(result.ErrorMessage);
            }
            return Results.NoContent();
        }).WithName("DeleteFunnel");

        group.MapPost("/calculate", async Task<IResult> ([FromBody] FunnelDefinitionDto dto, [FromQuery] int? days, [FromQuery] DateTime? from, [FromQuery] DateTime? to, IFunnelService funnelService, ClaimsPrincipal user, IPlanPolicy planPolicy, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);
            
            if (!await planPolicy.HasAdvancedAnalyticsAsync(userId, ct)) return Results.Forbid();

            var logger = loggerFactory.CreateLogger("FunnelEndpoints");
            using var act = TelemetryExtensions.StartActivity("api.funnels.calculate");
            
            var (start, end) = GetRange(from, to, days ?? 30);
            
            var result = await funnelService.CalculateFunnelAsync(userId, dto.SiteId, dto.Steps, start, end, ct);
            if (!result.IsSuccess) 
            { 
                if (result.ErrorType == ResultErrorType.Forbidden) return Results.Forbid();
                logger.LogWarning("Funnel calculation error: {Error}", result.ErrorMessage); 
                return Results.Problem(result.ErrorMessage); 
            }
            return Results.Ok(result.Value);
        }).WithName("CalculateFunnel");
    }

    private static (DateTime Start, DateTime End) GetRange(DateTime? from, DateTime? to, int days)
    {
        if (from.HasValue && to.HasValue) return (from.Value.ToUniversalTime(), to.Value.ToUniversalTime());
        
        if (days <= 0) days = 30;
        var today = DateTime.UtcNow.Date;
        var end = today.AddDays(1);
        var start = today.AddDays(-(days - 1));
        return (start, end);
    }
}
