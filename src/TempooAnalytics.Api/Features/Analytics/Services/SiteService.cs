using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Services;

public class SiteService(ISiteRepository siteRepo, IPlanPolicy planPolicy) : ISiteService
{
    public async Task<Result<object>> CreateSiteAsync(UserId ownerId, CreateSiteRequest request, CancellationToken ct)
    {
        // 1. Check Subscription Limits
        if (!await planPolicy.CanCreateSiteAsync(ownerId, ct))
        {
            var maxSites = await planPolicy.MaxSitesAsync(ownerId, ct);
            return Result.Failure<object>($"You have reached the maximum limit of {maxSites} sites for your current plan.", ResultErrorType.Failure);
        }

        var siteKey = request.SiteKey?.ToLowerInvariant().Trim();
        if (string.IsNullOrWhiteSpace(siteKey))
        {
            siteKey = GenerateSiteKey(request.Name);
        }

        if (await siteRepo.ExistsAsync(siteKey, ct))
        {
            return Result.Failure<object>($"The domain or site key '{siteKey}' is already registered.", ResultErrorType.Conflict);
        }

        var writeKey = GenerateWriteKey();

        var site = new AnalyticsSite
        {
            Id = SiteId.New(),
            SiteKey = siteKey,
            Name = request.Name,
            WriteKey = writeKey,
            CreatedAt = DateTime.UtcNow,
            OwnerId = ownerId,
            AllowedOrigins = request.AllowedOrigins ?? Array.Empty<string>(),
            IsDeleted = false
        };

        var result = await siteRepo.CreateSiteAsync(site, ct);

        return Result.Success<object>(new
        {
            id = result.Id.Value,
            siteKey = result.SiteKey,
            name = result.Name,
            writeKey = result.WriteKey
        });
    }

    public async Task<(string siteKey, string writeKey)> AdminGenerateWriteKeyAsync(string siteKey, string? adminUser, CancellationToken ct)
    {
        var result = await siteRepo.GenerateWriteKeyAsync(siteKey, UserId.Empty, ct);
        return (result.siteKey, result.writeKey);
    }

    public async Task<(string siteKey, string writeKey)> AdminRotateWriteKeyAsync(string siteKey, string? adminUser, CancellationToken ct)
    {
        var result = await siteRepo.RotateWriteKeyAsync(siteKey, UserId.Empty, ct);
        if (result == null)
        {
            var genResult = await siteRepo.GenerateWriteKeyAsync(siteKey, UserId.Empty, ct);
            return (genResult.siteKey, genResult.writeKey);
        }
        return result.Value;
    }

    public async Task<bool> AdminRevokeWriteKeyAsync(string siteKey, string? adminUser, CancellationToken ct)
    {
        var result = await siteRepo.RevokeWriteKeyAsync(siteKey, UserId.Empty, ct);
        return result != null;
    }

    public async Task<bool> DeleteSiteAsync(UserId ownerId, string siteKey, CancellationToken ct)
    {
        return await siteRepo.DeleteAsync(ownerId, siteKey, ct);
    }

    public async Task<Result<(string siteKey, string writeKey)>> GenerateWriteKeyAsync(UserId userId, string siteKey, CancellationToken ct)
    {
        var result = await siteRepo.GenerateWriteKeyAsync(siteKey, userId, ct);
        if (result.ownerId.HasValue && result.ownerId.Value != userId)
        {
            return Result.Failure<(string siteKey, string writeKey)>("Not the owner of this site", ResultErrorType.Unauthorized);
        }
        return Result.Success((result.siteKey, result.writeKey));
    }

    public async Task<(string siteKey, string writeKey)?> RotateWriteKeyAsync(UserId userId, string siteKey, CancellationToken ct)
    {
        var result = await siteRepo.RotateWriteKeyAsync(siteKey, userId, ct);
        return result != null ? (result.Value.siteKey, result.Value.writeKey) : null;
    }

    public async Task<string?> RevokeWriteKeyAsync(UserId userId, string siteKey, CancellationToken ct)
    {
        return await siteRepo.RevokeWriteKeyAsync(siteKey, userId, ct);
    }

    public async Task<SiteDto?> GetSiteAsync(string siteKey, CancellationToken ct)
    {
        var site = await siteRepo.GetSiteByKeyAsync(siteKey, ct);
        if (site == null) return null;

        return new SiteDto
        {
            Id = site.Id.Value,
            SiteKey = site.SiteKey,
            Name = site.Name,
            WriteKey = site.WriteKey,
            CreatedAt = site.CreatedAt,
            OwnerId = site.OwnerId?.Value,
            AllowedOrigins = site.AllowedOrigins
        };
    }

    private string GenerateWriteKey()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private string GenerateSiteKey(string name)
    {
        var key = name.ToLowerInvariant().Replace(" ", "-").Replace("_", "-");
        key = System.Text.RegularExpressions.Regex.Replace(key, "[^a-z0-9-]", "");
        var suffix = Convert.ToBase64String(RandomNumberGenerator.GetBytes(4)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return $"{key}-{suffix}";
    }
}
