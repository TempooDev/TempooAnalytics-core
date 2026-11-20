using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Auth.Dtos;

namespace TempooAnalytics.Api.Features.Auth.Services;

/// <summary>
/// First run of a self-host: the instance has no users and no public sign-up, so nobody could ever log in.
/// This creates the first administrator once, only when the host is community and the users table is empty.
/// The account comes from <c>ADMIN_USERNAME</c>/<c>ADMIN_PASSWORD</c> (or <c>Bootstrap:AdminUsername</c>/<c>Bootstrap:AdminPassword</c>);
/// without a password a random one is generated and written to the log once. A password given in configuration is never logged.
/// It goes through <see cref="IUserManagementService.SignupAsync"/> without a welcome email, so nothing depends on the mail provider.
/// </summary>
public static class FirstAdminBootstrapper
{
    public const string DefaultUsername = "admin";
    public const int MinPasswordLength = 8;
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    private const int GeneratedPasswordLength = 24;

    /// <returns>True when an administrator was created.</returns>
    public static async Task<bool> RunAsync(AnalyticsDbContext context, IUserManagementService users, IConfiguration config, ILogger logger, bool isCommunity)
    {
        if (!isCommunity) return false;
        // Not even a look at the configuration if there is somebody already: never recreates or resets anything.
        if (await context.Users.AnyAsync()) return false;

        var username = (FirstNonEmpty(config["ADMIN_USERNAME"], config["Bootstrap:AdminUsername"]) ?? DefaultUsername).Trim().ToLowerInvariant();
        var configuredPassword = FirstNonEmpty(config["ADMIN_PASSWORD"], config["Bootstrap:AdminPassword"]);
        var generated = configuredPassword is null;
        var password = configuredPassword ?? GeneratePassword();

        if (password.Length < MinPasswordLength)
            throw new InvalidOperationException($"ADMIN_PASSWORD must have at least {MinPasswordLength} characters; the first administrator was not created.");

        var result = await users.SignupAsync(new SignupRequest(username, password, null, IsAdmin: true), sendWelcomeEmail: false);
        if (!result.IsSuccess)
            throw new InvalidOperationException($"The first administrator '{username}' could not be created: {result.ErrorMessage}");

        if (generated)
        {
            logger.LogWarning(
                "FIRST RUN: no users existed, so the administrator '{Username}' was created with the generated password below. " +
                "It is shown only this once: log in and change it now (or set ADMIN_USERNAME and ADMIN_PASSWORD before the first start). Password: {Password}",
                username, password);
        }
        else
        {
            logger.LogInformation("FIRST RUN: no users existed, so the administrator '{Username}' was created from ADMIN_USERNAME/ADMIN_PASSWORD.", username);
        }
        return true;
    }

    public static string GeneratePassword() => RandomNumberGenerator.GetString(PasswordAlphabet, GeneratedPasswordLength);

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
