using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Features.Auth.Services;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Shared.Telemetry;

namespace TempooAnalytics.Api.Features.Auth.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app, bool isDevelopment)
    {
        var group = app.MapGroup("/auth").WithTags("Authentication");

        if (isDevelopment) group.MapPost("/token", CreateDevToken).WithName("DevToken");
        group.MapPost("/admin-session", CreateAdminSession).WithName("AdminSession");
        group.MapPost("/login", Login).WithName("Login");
        group.MapPost("/refresh", Refresh).WithName("Refresh");
        group.MapPost("/forgot-password", ForgotPassword).WithName("ForgotPassword");
        group.MapPost("/logout", Logout).WithName("Logout");
    }

    private static async Task<IResult> CreateDevToken([FromBody] TokenRequest req, IAuthService authService) => Results.Ok(await authService.CreateDevTokenAsync(req));

    private static async Task<IResult> CreateAdminSession([FromBody] AdminSessionRequest req, IAuthService authService)
    {
        var result = await authService.CreateAdminSessionAsync(req);
        return result.IsSuccess ? Results.Ok(result.Value) : Results.Unauthorized();
    }

    private static async Task<IResult> Login([FromBody] LoginRequest login, IAuthService authService, ILoggerFactory loggerFactory, HttpContext ctx, HttpResponse response)
    {
        var logger = loggerFactory.CreateLogger("AuthEndpoints");
        if (string.IsNullOrWhiteSpace(login.Username) || string.IsNullOrWhiteSpace(login.Password))
        {
            logger.LogWarning("Login failed: missing username or password");
            return Results.BadRequest(new { error = "username and password required" });
        }

        var result = await authService.LoginAsync(login);
        
        if (!result.IsSuccess)
        {
            logger.LogAuth("login", null, login.Username, false, ctx);
            return Results.Unauthorized();
        }

        logger.LogAuth("login", null, login.Username, true, ctx);
        var refreshToken = result.Value!.RefreshToken;
        // "Remember me": the cookie outlives the browser session until the refresh JWT itself expires.
        DateTimeOffset? expires = login.RememberMe
            ? new DateTimeOffset(DateTime.SpecifyKind(new JwtSecurityTokenHandler().ReadJwtToken(refreshToken).ValidTo, DateTimeKind.Utc))
            : null;
        AppendRefreshCookie(response, refreshToken, expires);
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> Refresh(HttpRequest request, HttpResponse response, IAuthService authService, ILoggerFactory loggerFactory, HttpContext ctx)
    {
        var logger = loggerFactory.CreateLogger("AuthEndpoints");
        string? refresh = request.Cookies.TryGetValue("refresh_token", out var c) ? c : null;
        if (string.IsNullOrWhiteSpace(refresh))
        {
            try
            {
                var body = await request.ReadFromJsonAsync<RefreshRequest>();
                refresh = body?.RefreshToken;
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(refresh))
        {
            logger.LogDebug("Refresh failed: missing refresh token");
            return Results.Unauthorized();
        }

        var result = await authService.RefreshAsync(refresh);
        
        if (!result.IsSuccess)
        {
            logger.LogAuth("refresh", null, null, false, ctx);
            DeleteRefreshCookie(response);
            return Results.Unauthorized();
        }

        logger.LogAuth("refresh", null, null, true, ctx);
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> ForgotPassword([FromBody] ForgotPasswordRequest req, IAuthService authService)
    {
        var result = await authService.ForgotPasswordAsync(req.Email);
        return result.IsSuccess ? Results.Ok() : Results.BadRequest(new { error = result.ErrorMessage });
    }

    private static IResult Logout(HttpResponse response, ILoggerFactory loggerFactory, HttpContext ctx)
    {
        var logger = loggerFactory.CreateLogger("AuthEndpoints");
        logger.LogAuth("logout", null, null, true, ctx);
        DeleteRefreshCookie(response);
        return Results.Ok();
    }

    // Without expires it is a session cookie: the browser drops it when it closes, so the session
    // does not survive a restart. With "remember me" it expires with the refresh JWT (30 days after login).
    // Refresh never re-sends it, so a remembered session is not extended by use.
    private static void AppendRefreshCookie(HttpResponse response, string refreshToken, DateTimeOffset? expires) =>
        response.Cookies.Append("refresh_token", refreshToken, new CookieOptions
        {
            Expires = expires,
            HttpOnly = true,
            Secure = true, // Keep true if you use HTTPS in development, or make it conditional if necessary
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });

    // Must match the options used in AppendRefreshCookie (notably Path), or the browser keeps the cookie.
    private static void DeleteRefreshCookie(HttpResponse response) =>
        response.Cookies.Delete("refresh_token", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });
}
