using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Services;

public interface ISiteAccessService
{
    /// <summary>True when the user owns the site or has been granted access to it.</summary>
    Task<bool> CanAccessAsync(UserId userId, SiteId siteId, CancellationToken ct = default);
}

/// <summary>
/// Site ownership or membership in analytics_user_sites, the same sites IUserRepository.ListSitesForUserAsync lists.
/// The AnalyticsSite query filter denies soft-deleted sites to both.
/// </summary>
public sealed class SiteAccessService(AnalyticsDbContext db) : ISiteAccessService
{
    public async Task<bool> CanAccessAsync(UserId userId, SiteId siteId, CancellationToken ct = default) =>
        await db.AnalyticsSites.AnyAsync(s => s.Id == siteId
            && (s.OwnerId == userId || db.UserSites.Any(us => us.UserId == userId && us.SiteId == s.Id)), ct);
}
