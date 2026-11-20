using System.Security.Claims;
using Microsoft.Extensions.Logging;
using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Features.Auth.Services;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Auth.Endpoints;

public static class UserAccountEndpoints
{
    public static void MapUserAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth")
            .RequireAuthorization()
            .WithTags("User Account");

        group.MapGet("/me", GetMe).WithName("Me");
        group.MapGet("/sites", GetMySites).WithName("MySites");
        group.MapPost("/change-password", ChangePassword).WithName("ChangePassword");
    }

    private static async Task<IResult> GetMe(IUserManagementService userManagement, IEnumerable<IProfileContributor> contributors, ClaimsPrincipal user, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("UserAccountEndpoints");
        var userIdClaim = user.GetUserId();
        if (userIdClaim == null)
        {
            logger.LogWarning("GetMe failed: sub claim not found");
            return Results.Unauthorized();
        }

        logger.LogInformation("Retrieving profile for user {UserId}", userIdClaim.Value);
        
        var userId = new UserId(userIdClaim.Value);

        var result = await userManagement.GetUserProfileAsync(userId);
        
        if (!result.IsSuccess)
        {
            logger.LogWarning("Profile not found for user {UserId}", userIdClaim.Value);
            return Results.NotFound();
        }

        var extra = new Dictionary<string, object?>();
        foreach (var contributor in contributors)
        {
            await contributor.ContributeAsync(userId, extra, ct);
        }

        return Results.Ok(extra.Count == 0 ? result.Value : result.Value with { Extra = extra });
    }

    private static async Task<IResult> GetMySites(IUserManagementService userManagement, ClaimsPrincipal user, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("UserAccountEndpoints");
        var userIdClaim = user.GetUserId();
        if (userIdClaim == null)
        {
            logger.LogWarning("GetMySites failed: sub claim not found");
            return Results.Unauthorized();
        }

        logger.LogInformation("Retrieving sites for user {UserId}", userIdClaim.Value);
        return Results.Ok(await userManagement.GetUserSitesAsync(new UserId(userIdClaim.Value)));
    }

    private static async Task<IResult> ChangePassword(ChangePasswordRequest req, IUserManagementService userManagement, ClaimsPrincipal user, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("UserAccountEndpoints");
        var userIdClaim = user.GetUserId();
        if (userIdClaim == null)
        {
            logger.LogWarning("ChangePassword failed: sub claim not found");
            return Results.Unauthorized();
        }

        logger.LogInformation("Change password attempt for user {UserId}", userIdClaim.Value);
        var success = await userManagement.ChangePasswordAsync(new UserId(userIdClaim.Value), req.OldPassword, req.NewPassword);
        
        if (!success)
        {
            logger.LogWarning("Change password failed for user {UserId}: invalid old password", userIdClaim.Value);
            return Results.BadRequest(new { error = "invalid credentials" });
        }

        logger.LogInformation("Password changed successfully for user {UserId}", userIdClaim.Value);
        return Results.Ok();
    }
}
