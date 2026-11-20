using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Services;

public interface ISiteService : IService
{
    Task<Result<object>> CreateSiteAsync(UserId ownerId, CreateSiteRequest request, CancellationToken ct);
    Task<(string siteKey, string writeKey)> AdminGenerateWriteKeyAsync(string siteKey, string? adminUser, CancellationToken ct);
    Task<(string siteKey, string writeKey)> AdminRotateWriteKeyAsync(string siteKey, string? adminUser, CancellationToken ct);
    Task<bool> AdminRevokeWriteKeyAsync(string siteKey, string? adminUser, CancellationToken ct);
    Task<bool> DeleteSiteAsync(UserId ownerId, string siteKey, CancellationToken ct);

    // Write key management for owners
    Task<Result<(string siteKey, string writeKey)>> GenerateWriteKeyAsync(UserId userId, string siteKey, CancellationToken ct);
    Task<(string siteKey, string writeKey)?> RotateWriteKeyAsync(UserId userId, string siteKey, CancellationToken ct);
    Task<string?> RevokeWriteKeyAsync(UserId userId, string siteKey, CancellationToken ct);
    Task<SiteDto?> GetSiteAsync(string siteKey, CancellationToken ct);
}
