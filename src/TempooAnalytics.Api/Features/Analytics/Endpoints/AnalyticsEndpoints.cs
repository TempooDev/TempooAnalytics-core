using System.Security.Claims;
using FluentValidation;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Shared.Validation;

namespace TempooAnalytics.Api.Features.Analytics.Endpoints;

public static class AnalyticsEndpoints
{
    public static void MapAnalyticsIngestEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/analytics/events", async (
            AnalyticsEventDto dto,
            HttpRequest request,
            IIngestionService ingestionService,
            CancellationToken ct) =>
        {
            var result = await ingestionService.ProcessEventAsync(dto, request, ct);
            if (!result.IsSuccess)
            {
                return result.ErrorType switch
                {
                    ResultErrorType.Unauthorized => Results.StatusCode(StatusCodes.Status403Forbidden),
                    _ => Results.BadRequest(new { message = result.ErrorMessage })
                };
            }
            
            return Results.Accepted();
        })
        .AllowAnonymous()
        .AddEndpointFilter<ValidateRequestFilter<AnalyticsEventDto>>()
        .RequireCors("PublicIngest")
        .WithTags("Ingestion")
        .WithName("IngestAnalyticsEvent");
    }

    public static void MapAnalyticsDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/analytics").WithTags("Analytics Data");

        group.MapGet("/sites", async (
            IMetricsService metricsService,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);

            var sites = await metricsService.GetSitesSummaryAsync(userId, ct);
            return Results.Ok(sites);
        })
        .RequireAuthorization()
        .WithName("GetSitesSummary");

        group.MapGet("/sites/{siteKey}/daily", async (
            string siteKey,
            HttpRequest request,
            IMetricsService metricsService,
            IValidator<DaysQueryDto> validator,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);

            var query = new DaysQueryDto { Days = int.TryParse(request.Query["days"].FirstOrDefault(), out var d) ? d : null };
            var validation = await validator.ValidateAsync(query, ct);
            if (!validation.IsValid)
            {
                var errors = validation.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                return Results.ValidationProblem(errors);
            }

            var effectiveDays = query.Days.GetValueOrDefault(30);
            if (effectiveDays <= 0) effectiveDays = 30;

            var metrics = await metricsService.GetDailyMetricsAsync(userId, siteKey, effectiveDays, ct);
            return Results.Ok(metrics);
        })
        .RequireAuthorization()
        .WithName("GetSiteDailyMetrics");
    }
}
