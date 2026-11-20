using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Shared.Telemetry;

namespace TempooAnalytics.Api.Auth;

/// <summary>
/// Users and sites, through the core context. What a module keeps per user (subscriptions, invoices, payment-provider customers) is not
/// here: the plan the core needs to know about goes through <see cref="IUserPlanStore"/>, the rest through the module's own store.
/// </summary>
public class UserRepository(AnalyticsDbContext dbContext, IUserPlanStore plans) : IUserRepository
{
    // Core context only. A module that must span its own tables in the same transaction (the cloud billing store) enlists
    // its context in the one begun here (they share the scope's connection, see RequestDbConnection).
    public async Task BeginTransactionAsync() => await dbContext.Database.BeginTransactionAsync();

    public async Task CommitTransactionAsync() => await dbContext.Database.CommitTransactionAsync();

    public async Task RollbackTransactionAsync() => await dbContext.Database.RollbackTransactionAsync();

    public async Task<User?> FindByUsernameAsync(string username)
    {
        using var act = TelemetryExtensions.StartActivity("repo.user.findByUsername");

        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Username == username);

        if (user is not null)
        {
            act?.SetTagSafe(TelemetryConstants.TagUserId, user.Id.Value);
        }

        return user;
    }

    public async Task<User?> FindByEmailAsync(string email) =>
        await dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);

    public async Task<bool> IsEmailTakenAsync(string email, UserId? excludeUserId = null)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var query = dbContext.Users.Where(u => u.Email == email);
        if (excludeUserId != null) query = query.Where(u => u.Id != excludeUserId);
        return await query.AnyAsync();
    }

    public async Task<bool> IsUsernameTakenAsync(string username, UserId? excludeUserId = null)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        var query = dbContext.Users.Where(u => u.Username == username);
        if (excludeUserId != null) query = query.Where(u => u.Id != excludeUserId);
        return await query.AnyAsync();
    }
    public Task<User?> FindByIdAsync(UserId id) => 
        dbContext.Users.FirstOrDefaultAsync(u => u.Id == id);

    public async Task<IEnumerable<SiteId>> ListSitesForUserAsync(UserId userId) => 
        await SitesForUser(userId)
            .Select(s => s.Id)
            .ToListAsync();

    public async Task<IEnumerable<UserSiteDto>> GetUserSitesDetailAsync(UserId userId) => 
        await SitesForUser(userId)
            .Select(s => new UserSiteDto(s.Id, s.SiteKey, s.Name))
            .ToListAsync();

    // Owned sites plus the ones shared through analytics_user_sites. Sites created through the API only set
    // OwnerId (no UserSites row). The AnalyticsSite query filter leaves out soft-deleted sites.
    private IQueryable<AnalyticsSite> SitesForUser(UserId userId) =>
        dbContext.AnalyticsSites.Where(s =>
            s.OwnerId == userId || dbContext.UserSites.Any(us => us.UserId == userId && us.SiteId == s.Id));

    public async Task<bool> ValidateCredentialsAsync(string username, string password) 
    { 
        var user = await FindByUsernameAsync(username); 
        return user is not null && PasswordHelper.VerifyPassword(password, user.PasswordHash); 
    }

    public async Task CreateUserAsync(User user, string? password = null, string? token = null, DateTime? expires = null)
    {
        bool isActive = !string.IsNullOrEmpty(password);
        
        user.PasswordHash = isActive 
            ? PasswordHelper.HashPassword(password!) 
            : "PENDING_ACTIVATION";
            
        user.IsActive = isActive;
        user.ActivationToken = token;
        user.ActivationExpires = expires;
        user.CreatedAt = DateTime.UtcNow;
        
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
    }

    public async Task UpdateUserAsync(User user) 
    { 
        dbContext.Users.Update(user); 
        await dbContext.SaveChangesAsync(); 
    }

    public async Task DeleteUserAsync(UserId id)
    {
        var user = await dbContext.Users.FindAsync(id);
        if (user == null) return;

        // One transaction for everything that goes with the user: their site links, plan data (bulk deletes that run
        // at once) and the user itself. If removing the user fails, nothing is left half deleted. When the caller already
        // opened a transaction, this joins it and leaves committing to the caller.
        var ownsTransaction = dbContext.Database.CurrentTransaction == null;
        if (ownsTransaction) await BeginTransactionAsync();
        try
        {
            await dbContext.UserSites.Where(us => us.UserId == id).ExecuteDeleteAsync();
            await plans.RemoveUserPlanAsync(id);

            dbContext.Users.Remove(user);
            await dbContext.SaveChangesAsync();

            if (ownsTransaction) await CommitTransactionAsync();
        }
        catch
        {
            if (ownsTransaction) await RollbackTransactionAsync();
            throw;
        }
    }

    public async Task<bool> ChangePasswordAsync(UserId userId, string oldPassword, string newPassword)
    {
        var user = await dbContext.Users.FindAsync(userId);
        
        if (user is null || !PasswordHelper.VerifyPassword(oldPassword, user.PasswordHash)) 
        {
            return false;
        }

        user.PasswordHash = PasswordHelper.HashPassword(newPassword);
        await dbContext.SaveChangesAsync();
        
        return true;
    }

    public async Task ForceResetPasswordAsync(UserId userId, string newPassword)
    {
        var user = await dbContext.Users.FindAsync(userId);
        if (user != null)
        {
            user.PasswordHash = PasswordHelper.HashPassword(newPassword);
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task AssignSiteToUserAsync(UserId userId, SiteId siteId) 
    { 
        dbContext.UserSites.Add(new UserSite { UserId = userId, SiteId = siteId }); 
        try 
        { 
            await dbContext.SaveChangesAsync(); 
        } 
        catch (DbUpdateException) 
        { 
            // Ignore duplicates
        } 
    }

    public async Task<List<User>> GetAllUsersAsync() => 
        await dbContext.Users
            .AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();
}
