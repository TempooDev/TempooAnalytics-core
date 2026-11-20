using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Repositories;

public interface ISiteRepository
{
    Task<(string siteKey, string writeKey, UserId? ownerId)> GenerateWriteKeyAsync(string siteKey, UserId ownerId, CancellationToken ct = default);
    Task<(string siteKey, string writeKey)?> RotateWriteKeyAsync(string siteKey, UserId ownerId, CancellationToken ct = default);
    Task<string?> RevokeWriteKeyAsync(string siteKey, UserId ownerId, CancellationToken ct = default);

    Task<AnalyticsSite?> GetSiteByKeyAsync(string siteKey, CancellationToken ct = default);
    Task<AnalyticsSite?> GetSiteByIdAsync(SiteId siteId, CancellationToken ct = default);
    Task<AnalyticsSite> CreateSiteAsync(AnalyticsSite site, CancellationToken ct = default);
    Task<bool> ExistsAsync(string siteKey, CancellationToken ct = default);
    Task<int> CountOwnedSitesAsync(UserId ownerId, CancellationToken ct = default);
    Task<bool> DeleteAsync(UserId ownerId, string siteKey, CancellationToken ct = default);
}
