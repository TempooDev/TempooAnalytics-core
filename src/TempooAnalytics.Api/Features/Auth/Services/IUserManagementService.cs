using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Auth.Services;

public interface IUserManagementService : IService
{
    Task<Result<UserDto>> SignupAsync(SignupRequest signup, bool sendWelcomeEmail = true);
    Task<bool> ChangePasswordAsync(UserId userId, string oldPassword, string newPassword);
    Task<Result<UserProfileDto>> GetUserProfileAsync(UserId userId);
    Task<IEnumerable<UserSiteDto>> GetUserSitesAsync(UserId userId);
    Task<IEnumerable<UserDto>> GetAllUsersAsync();
}
