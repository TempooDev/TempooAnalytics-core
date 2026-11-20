using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Features.Auth.Services;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Email;

namespace TempooAnalytics.Api.Features.Auth.Endpoints;

public static class UserAdminEndpoints
{
    public static void MapUserAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin")
            .RequireAuthorization("AdminPolicy")
            .WithTags("Admin: Users");

        admin.MapGet("/users", async (IUserManagementService um, ILoggerFactory loggerFactory) => {
            var logger = loggerFactory.CreateLogger("UserAdminEndpoints");
            logger.LogInformation("Admin: Retrieving all users");
            return Results.Ok(await um.GetAllUsersAsync());
        }).WithName("GetAllUsers");
        
        admin.MapPost("/signup", Signup).WithName("Signup");
        
        admin.MapPost("/users/{userId:guid}/sites", async (Guid userId, [FromBody] AssignSiteRequest req, IUserRepository users, ILoggerFactory loggerFactory) => {
            var logger = loggerFactory.CreateLogger("UserAdminEndpoints");
            logger.LogInformation("Admin: Assigning site {SiteId} to user {UserId}", req.SiteId, userId);
            await users.AssignSiteToUserAsync(new UserId(userId), new SiteId(req.SiteId));
            return Results.Ok();
        });
        
        admin.MapGet("/users/{userId:guid}/sites", async (Guid userId, IUserRepository users, ILoggerFactory loggerFactory) => {
            var logger = loggerFactory.CreateLogger("UserAdminEndpoints");
            logger.LogInformation("Admin: Retrieving sites for user {UserId}", userId);
            return Results.Ok(await users.ListSitesForUserAsync(new UserId(userId)));
        });
    }

    private static async Task<IResult> Signup([FromBody] SignupRequest req, IUserManagementService userManagement, IEmailService emailService, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("UserAdminEndpoints");
        
        // Normalize input
        var normalizedReq = req with { 
            Username = req.Username.ToLowerInvariant().Trim(), 
            Email = req.Email?.ToLowerInvariant().Trim() 
        };

        logger.LogInformation("Admin: Creating user {Username} ({Email})", normalizedReq.Username, normalizedReq.Email);
        var result = await userManagement.SignupAsync(normalizedReq);
        
        if (!result.IsSuccess)
        {
            logger.LogWarning("Admin: User creation failed for {Username}: {Error}", normalizedReq.Username, result.ErrorMessage);
            return result.ErrorType == ResultErrorType.Conflict 
                ? Results.Conflict(new { error = result.ErrorMessage }) 
                : Results.BadRequest(new { error = result.ErrorMessage });
        }

        // We send the unified email with the credentials
        if (!string.IsNullOrWhiteSpace(normalizedReq.Email))
        {
            try
            {
                await emailService.SendWelcomeWithCredentialsEmailAsync(normalizedReq.Email, normalizedReq.Username, normalizedReq.Password);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Admin: Failed to send welcome email to {Email}", normalizedReq.Email);
            }
        }

        logger.LogInformation("Admin: User {Username} created successfully with Id {UserId}", req.Username, result.Value!.Id);
        return Results.Created($"/users/{result.Value!.Id}", result.Value);
    }
}
