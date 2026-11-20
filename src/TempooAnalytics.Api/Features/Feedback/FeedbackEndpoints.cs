using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Feedback.Models;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Services;

namespace TempooAnalytics.Api.Features.Feedback;

public static class FeedbackEndpoints
{
    public record CreateFeedbackRequest(string Title, string Description);

    public static void MapFeedbackEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/feedback").WithTags("Feedback").RequireAuthorization();

        group.MapPost("/", async (CreateFeedbackRequest request, AnalyticsDbContext db, ClaimsPrincipal user, IAdminAlertService adminAlerts) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userIdGuid))
            {
                return Results.Unauthorized();
            }

            var username = user.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown User";

            var feedback = new Models.Feedback
            {
                UserId = new UserId(userIdGuid),
                Title = request.Title,
                Description = request.Description,
                CreatedAt = DateTime.UtcNow
            };

            db.Feedbacks.Add(feedback);
            await db.SaveChangesAsync();

            await adminAlerts.AlertAsync(AdminAlerts.FeedbackSubmitted(username, request.Title, request.Description));

            return Results.Ok();
        });
    }
}
