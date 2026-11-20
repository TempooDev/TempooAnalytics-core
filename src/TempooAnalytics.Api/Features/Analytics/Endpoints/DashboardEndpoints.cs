using System.Security.Claims;
using System.Text.Json;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/dashboard")
            .RequireAuthorization()
            .WithTags("Analytics Dashboard");

        group.MapGet("/", async Task<IResult> (HttpRequest request, ClaimsPrincipal user, IDashboardRepository repo, IPlanPolicy planPolicy, CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);

            var days = int.TryParse(request.Query["days"].FirstOrDefault(), out var d) ? d : 30;
            if (days <= 0) days = 30;
            
            DateTime? from = DateTime.TryParse(request.Query["from"].FirstOrDefault(), out var f) ? f.ToUniversalTime() : null;
            DateTime? to = DateTime.TryParse(request.Query["to"].FirstOrDefault(), out var t) ? t.ToUniversalTime() : null;

            var top = int.TryParse(request.Query["top"].FirstOrDefault(), out var tVal) ? tVal : 5;
            var siteKey = request.Query["siteKey"].FirstOrDefault();
            var breakdown = request.Query["breakdown"].FirstOrDefault();
            
            // Robust filter parsing (supports 'filter' or 'filters' and plain arrays)
            AnalyticsFilterDto? filter = null;
            var filterJson = request.Query["filters"].FirstOrDefault() ?? request.Query["filter"].FirstOrDefault();
            
            if (!string.IsNullOrWhiteSpace(filterJson))
            {
                try 
                {
                    // If the JSON starts with '[', it is an array of AdvancedFilters
                    if (filterJson.TrimStart().StartsWith('['))
                    {
                        var advanced = JsonSerializer.Deserialize<List<AdvancedFilterDto>>(filterJson, AppJsonContext.Default.ListAdvancedFilterDto);
                        filter = new AnalyticsFilterDto { AdvancedFilters = advanced ?? new() };
                    }
                    else
                    {
                        filter = JsonSerializer.Deserialize<AnalyticsFilterDto>(filterJson, AppJsonContext.Default.AnalyticsFilterDto);
                    }
                }
                catch { /* Ignore parse errors from malformed filters */ }
            }

            // ENFORCEMENT: Check if user has permission for advanced filters
            if (filter != null && filter.AdvancedFilters.Any())
            {
                if (!await planPolicy.HasAdvancedAnalyticsAsync(userId, ct))
                {
                    return Results.Forbid();
                }
            }

            var dto = await repo.GetDashboardAsync(userId, from, to, days, top, siteKey, filter, breakdown, ct);
            return Results.Ok(dto);
        })
        .WithName("GetDashboard");

        group.MapGet("/referrers", async (HttpRequest request, ClaimsPrincipal user, IDashboardRepository repo, CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);

            var days = int.TryParse(request.Query["days"].FirstOrDefault(), out var d) ? d : 30;
            DateTime? from = DateTime.TryParse(request.Query["from"].FirstOrDefault(), out var f) ? f.ToUniversalTime() : null;
            DateTime? to = DateTime.TryParse(request.Query["to"].FirstOrDefault(), out var t) ? t.ToUniversalTime() : null;
            var siteKey = request.Query["siteKey"].FirstOrDefault();

            AnalyticsFilterDto? filter = null;
            var filterJson = request.Query["filters"].FirstOrDefault() ?? request.Query["filter"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(filterJson))
            {
                try
                {
                    if (filterJson.TrimStart().StartsWith('['))
                    {
                        var advanced = JsonSerializer.Deserialize<List<AdvancedFilterDto>>(filterJson, AppJsonContext.Default.ListAdvancedFilterDto);
                        filter = new AnalyticsFilterDto { AdvancedFilters = advanced ?? new() };
                    }
                    else
                    {
                        filter = JsonSerializer.Deserialize<AnalyticsFilterDto>(filterJson, AppJsonContext.Default.AnalyticsFilterDto);
                    }
                }
                catch { }
            }

            var referrers = await repo.GetReferrersDetailsAsync(userId, from, to, days, siteKey, filter, ct);
            return Results.Ok(referrers);
        })
        .WithName("GetReferrersDetails");

        group.MapGet("/channels", async (HttpRequest request, ClaimsPrincipal user, IDashboardRepository repo, CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);

            var days = int.TryParse(request.Query["days"].FirstOrDefault(), out var d) ? d : 30;
            DateTime? from = DateTime.TryParse(request.Query["from"].FirstOrDefault(), out var f) ? f.ToUniversalTime() : null;
            DateTime? to = DateTime.TryParse(request.Query["to"].FirstOrDefault(), out var t) ? t.ToUniversalTime() : null;
            var siteKey = request.Query["siteKey"].FirstOrDefault();

            AnalyticsFilterDto? filter = null;
            var filterJson = request.Query["filters"].FirstOrDefault() ?? request.Query["filter"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(filterJson))
            {
                try
                {
                    if (filterJson.TrimStart().StartsWith('['))
                    {
                        var advanced = JsonSerializer.Deserialize<List<AdvancedFilterDto>>(filterJson, AppJsonContext.Default.ListAdvancedFilterDto);
                        filter = new AnalyticsFilterDto { AdvancedFilters = advanced ?? new() };
                    }
                    else
                    {
                        filter = JsonSerializer.Deserialize<AnalyticsFilterDto>(filterJson, AppJsonContext.Default.AnalyticsFilterDto);
                    }
                }
                catch { }
            }

            var channels = await repo.GetChannelsDetailsAsync(userId, from, to, days, siteKey, filter, ct);
            return Results.Ok(channels);
        })
        .WithName("GetChannelsDetails");

        group.MapGet("/pages", async (HttpRequest request, ClaimsPrincipal user, IDashboardRepository repo, CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);

            var type = request.Query["type"].FirstOrDefault() ?? "pages";
            var days = int.TryParse(request.Query["days"].FirstOrDefault(), out var d) ? d : 30;
            DateTime? from = DateTime.TryParse(request.Query["from"].FirstOrDefault(), out var f) ? f.ToUniversalTime() : null;
            DateTime? to = DateTime.TryParse(request.Query["to"].FirstOrDefault(), out var t) ? t.ToUniversalTime() : null;
            var siteKey = request.Query["siteKey"].FirstOrDefault();

            AnalyticsFilterDto? filter = null;
            var filterJson = request.Query["filters"].FirstOrDefault() ?? request.Query["filter"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(filterJson))
            {
                try
                {
                    if (filterJson.TrimStart().StartsWith('['))
                    {
                        var advanced = JsonSerializer.Deserialize<List<AdvancedFilterDto>>(filterJson, AppJsonContext.Default.ListAdvancedFilterDto);
                        filter = new AnalyticsFilterDto { AdvancedFilters = advanced ?? new() };
                    }
                    else
                    {
                        filter = JsonSerializer.Deserialize<AnalyticsFilterDto>(filterJson, AppJsonContext.Default.AnalyticsFilterDto);
                    }
                }
                catch { }
            }

            var pages = await repo.GetPagesDetailsAsync(userId, type, from, to, days, siteKey, filter, ct);
            return Results.Ok(pages);
        })
        .WithName("GetPagesDetails");

        group.MapGet("/events", async (HttpRequest request, ClaimsPrincipal user, IDashboardRepository repo, CancellationToken ct) =>
        {
            var userIdClaim = user.GetUserId();
            if (userIdClaim == null) return Results.Unauthorized();
            var userId = new UserId(userIdClaim.Value);

            var days = int.TryParse(request.Query["days"].FirstOrDefault(), out var d) ? d : 30;
            DateTime? from = DateTime.TryParse(request.Query["from"].FirstOrDefault(), out var f) ? f.ToUniversalTime() : null;
            DateTime? to = DateTime.TryParse(request.Query["to"].FirstOrDefault(), out var t) ? t.ToUniversalTime() : null;
            var siteKey = request.Query["siteKey"].FirstOrDefault();
            var limit = int.TryParse(request.Query["limit"].FirstOrDefault(), out var l) ? l : 100;
            var offset = int.TryParse(request.Query["offset"].FirstOrDefault(), out var o) ? o : 0;

            AnalyticsFilterDto? filter = null;
            var filterJson = request.Query["filters"].FirstOrDefault() ?? request.Query["filter"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(filterJson))
            {
                try
                {
                    if (filterJson.TrimStart().StartsWith('['))
                    {
                        var advanced = JsonSerializer.Deserialize<List<AdvancedFilterDto>>(filterJson, AppJsonContext.Default.ListAdvancedFilterDto);
                        filter = new AnalyticsFilterDto { AdvancedFilters = advanced ?? new() };
                    }
                    else
                    {
                        filter = JsonSerializer.Deserialize<AnalyticsFilterDto>(filterJson, AppJsonContext.Default.AnalyticsFilterDto);
                    }
                }
                catch { }
            }

            var events = await repo.GetEventsDetailsAsync(userId, from, to, days, siteKey, filter, limit, offset, ct);
            return Results.Ok(events);
        })
        .WithName("GetEventsDetails");
    }
}
