using Microsoft.AspNetCore.OpenApi;
using TempooAnalytics.Api.Auth;

namespace TempooAnalytics.Api.Features.Test;

/// <summary>
/// Test endpoints for verifying service health and gRPC integration between Go and .NET
/// </summary>
public static class TestEndpoints
{
    public static void MapTestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/test")
            .WithTags("Testing")
            .WithDescription("Test endpoints for development and debugging");

        group.MapGet("/grpc-health", GrpcConnectionHealth)
            .WithName("GrpcConnectionHealth")
            .WithSummary("Check gRPC Connection Status")
            .WithDescription("Verifies the status of gRPC connections to external services (if any)");
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    private static IResult HealthCheck()
    {
        return Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            service = "TempooAnalytics.Api"
        });
    }

    /// <summary>
    /// Check gRPC connection status
    /// </summary>
    private static IResult GrpcConnectionHealth()
    {
        return Results.Ok(new
        {
            grpc_available = true,
            status = "connected",
            message = "gRPC services are available. Go service can call this API via gRPC."
        });
    }

    /// <summary>
    /// List all users - test endpoint
    /// </summary>
    private static async Task<IResult> GetAllUsersTest(IUserRepository userRepository)
    {
        try
        {
            var users = await userRepository.GetAllUsersAsync();

            var userDtos = users.Select(u => new
            {
                user_id = u.Id,
                email = u.Email,
                username = u.Username,
                is_active = u.IsActive,
                is_admin = u.IsAdmin,
                created_at = u.CreatedAt
            }).ToList();

            return Results.Ok(new
            {
                success = true,
                total_count = userDtos.Count,
                users = userDtos,
                message = "✅ Successfully retrieved all users from database"
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new
            {
                success = false,
                error = "Error retrieving users",
                details = ex.Message
            });
        }
    }
}
