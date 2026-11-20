using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Email;

namespace TempooAnalytics.Api.Features.Auth.Services;

public class UserManagementService(
    IUserRepository userRepository,
    HybridCache cache,
    ILogger<UserManagementService> logger) : IUserManagementService
{
    public async Task<Result<UserDto>> SignupAsync(SignupRequest signup, bool sendWelcomeEmail = true)
    {
        logger.LogInformation("Signup attempt for user: {Username} ({Email})", signup.Username, signup.Email);

        var existing = await userRepository.FindByUsernameAsync(signup.Username);
        if (existing != null)
        {
            logger.LogWarning("Signup failed: Username already exists: {Username}", signup.Username);
            return Result.Failure<UserDto>("username already exists", ResultErrorType.Conflict);
        }

        try
        {
            var user = new User
            {
                Id = UserId.New(),
                Username = signup.Username,
                Email = signup.Email,
                IsAdmin = signup.IsAdmin,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };
            await userRepository.CreateUserAsync(user, signup.Password);

            logger.LogInformation("User created successfully: {Username} (Id: {UserId})", signup.Username, user.Id);

            return Result.Success(new UserDto(user.Id.Value, user.Username, user.Email, user.IsAdmin, user.IsActive, user.CreatedAt));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during signup for user: {Username}", signup.Username);
            return Result.Failure<UserDto>("An error occurred during signup", ResultErrorType.Failure);
        }
    }

    public async Task<bool> ChangePasswordAsync(UserId userId, string oldPassword, string newPassword)
    {
        return await userRepository.ChangePasswordAsync(userId, oldPassword, newPassword);
    }

    public async Task<Result<UserProfileDto>> GetUserProfileAsync(UserId userId)
    {
        var user = await userRepository.FindByIdAsync(userId);
        if (user == null) return Result.Failure<UserProfileDto>("User not found", ResultErrorType.NotFound);

        return Result.Success(new UserProfileDto(user.Id.Value, user.Username, user.Email, user.IsAdmin));
    }

    public async Task<IEnumerable<UserSiteDto>> GetUserSitesAsync(UserId userId)
    {
        return await userRepository.GetUserSitesDetailAsync(userId);
    }

    public async Task<IEnumerable<UserDto>> GetAllUsersAsync()
    {
        var users = await userRepository.GetAllUsersAsync();
        return users.Select(u => new UserDto(u.Id.Value, u.Username, u.Email, u.IsAdmin, u.IsActive, u.CreatedAt));
    }
}
