namespace TempooAnalytics.Api.Auth;

using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Infrastructure.Common;

public interface IUserRepository
{
    Task<User?> FindByUsernameAsync(string username);
    Task<User?> FindByEmailAsync(string email);
    Task<User?> FindByIdAsync(UserId id);
    Task<bool> IsEmailTakenAsync(string email, UserId? excludeUserId = null);
    Task<bool> IsUsernameTakenAsync(string username, UserId? excludeUserId = null);
    Task<IEnumerable<SiteId>> ListSitesForUserAsync(UserId userId);
    Task<IEnumerable<UserSiteDto>> GetUserSitesDetailAsync(UserId userId);
    Task<bool> ValidateCredentialsAsync(string username, string password);
    Task<List<User>> GetAllUsersAsync();

    Task CreateUserAsync(User user, string? password = null, string? token = null, DateTime? expires = null);
    Task<bool> ChangePasswordAsync(UserId userId, string oldPassword, string newPassword);
    Task ForceResetPasswordAsync(UserId userId, string newPassword);
    Task AssignSiteToUserAsync(UserId userId, SiteId siteId);

    Task UpdateUserAsync(User user);
    Task DeleteUserAsync(UserId id);

    Task BeginTransactionAsync();
    Task CommitTransactionAsync();
    Task RollbackTransactionAsync();
}
