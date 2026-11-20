using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Email;

namespace TempooAnalytics.Api.Features.Auth.Services;

public class AuthService(IUserRepository userRepository, IConfiguration config, ILogger<AuthService> logger, IEmailService emailService, IJwtKeyProvider jwtKeys) : IAuthService
{
    // How long a refresh JWT is valid; a remembered login's cookie expires with it.
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest login)
    {
        var username = login.Username.ToLowerInvariant().Trim();
        logger.LogInformation("Login attempt for user: {Username}", username);

        var valid = await userRepository.ValidateCredentialsAsync(username, login.Password);
        if (!valid)
        {
            logger.LogWarning("Login failed for user: {Username} (Invalid credentials)", username);
            return Result.Failure<AuthResponse>("Invalid credentials", ResultErrorType.Unauthorized);
        }

        var user = await userRepository.FindByUsernameAsync(username);
        if (user is null)
        {
            logger.LogWarning("Login failed for user: {Username} (User not found after validation - this should not happen)", username);
            return Result.Failure<AuthResponse>("User not found", ResultErrorType.Unauthorized);
        }

        logger.LogInformation("Login successful for user: {Username} (Id: {UserId})", username, user.Id);
        return Result.Success(GenerateAuthResponse(user.Id.ToString(), user.IsAdmin));
    }

    public async Task<Result<AuthResponse>> RefreshAsync(string refreshToken)
    {
        var jwtKey = jwtKeys.GetKey();
        // Keep the JWT claim names: by default the handler maps "sub" to ClaimTypes.NameIdentifier,
        // and the lookup below would never find it (every refresh failed with "Invalid token").
        var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        try
        {
            var principal = tokenHandler.ValidateToken(refreshToken, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ValidateIssuerSigningKey = true
            }, out var _);

            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (string.IsNullOrWhiteSpace(sub)) return Result.Failure<AuthResponse>("Invalid token", ResultErrorType.Unauthorized);

            bool isAdmin = principal.FindFirst("is_admin")?.Value == "true";

            return Result.Success(GenerateAuthResponse(sub, isAdmin));
        }
        catch (SecurityTokenExpiredException)
        {
            // Token is expired, expected behavior, don't spam logs with warnings.
            return Result.Failure<AuthResponse>("Refresh token expired", ResultErrorType.Unauthorized);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token refresh failed");
            return Result.Failure<AuthResponse>("Token refresh failed", ResultErrorType.Unauthorized);
        }
    }

    public async Task<Result> ForgotPasswordAsync(string email)
    {
        // Checked before any lookup so the answer is the same for every address (no account enumeration), and before
        // touching the password: without a mail provider the temporary password could never be delivered and the user
        // would be locked out of their own account.
        if (!emailService.IsConfigured)
        {
            logger.LogWarning("Password reset requested but no email provider is configured (Resend:ApiKey); nothing was changed.");
            return Result.Failure("Password reset by email is not available on this server. Ask an administrator to reset your password.");
        }

        email = email.ToLowerInvariant().Trim();
        logger.LogInformation("Password reset request for email: {Email}", email);
        
        var user = await userRepository.FindByUsernameAsync(email);
        if (user == null)
        {
            // Security: don't reveal if user exists
            logger.LogWarning("Password reset requested for non-existent user: {Email}", email);
            return Result.Success(); 
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            logger.LogWarning("User {Username} has no email associated, cannot reset password", user.Username);
            return Result.Failure("No email associated with this account");
        }

        var tempPassword = GenerateRandomPassword();
        await userRepository.ForceResetPasswordAsync(user.Id, tempPassword);
        
        try
        {
            await emailService.SendPasswordResetEmailAsync(user.Email, user.Username, tempPassword);
            logger.LogInformation("Password reset email sent to {Email}", user.Email);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send password reset email to {Email}", user.Email);
            return Result.Failure("Failed to send reset email");
        }

        return Result.Success();
    }

    public async Task<AuthResponse> CreateDevTokenAsync(TokenRequest req)
    {
        return GenerateAuthResponse(req.UserId, req.IsAdmin, req.DisplayName, 12 * 60);
    }

    public async Task<Result<AuthResponse>> CreateAdminSessionAsync(AdminSessionRequest req)
    {
        var masterPassword = config["Management:MasterPassword"];
        if (string.IsNullOrWhiteSpace(masterPassword) || req.Password != masterPassword)
        {
            return Result.Failure<AuthResponse>("Invalid master password", ResultErrorType.Unauthorized);
        }

        return Result.Success(GenerateAuthResponse("admin-master", true, "Master Admin", 120));
    }

    private static string GenerateRandomPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 10)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    private AuthResponse GenerateAuthResponse(string userId, bool isAdmin, string? displayName = null, int expiresMinutes = 30)
    {
        var jwtKey = jwtKeys.GetKey();
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim> { new Claim(JwtRegisteredClaimNames.Sub, userId) };
        if (isAdmin) claims.Add(new Claim("is_admin", "true"));
        if (!string.IsNullOrWhiteSpace(displayName)) claims.Add(new Claim("name", displayName));

        var now = DateTime.UtcNow;
        var accessJwt = new JwtSecurityToken(claims: claims, notBefore: now, expires: now.AddMinutes(expiresMinutes), signingCredentials: creds);
        var access = new JwtSecurityTokenHandler().WriteToken(accessJwt);

        var refreshJwt = new JwtSecurityToken(claims: claims, notBefore: now, expires: now.Add(RefreshTokenLifetime), signingCredentials: creds);
        var refresh = new JwtSecurityTokenHandler().WriteToken(refreshJwt);

        return new AuthResponse(access, refresh, 60 * expiresMinutes);
    }
}
