using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Shared.Telemetry;

namespace TempooAnalytics.Api.Features.Analytics.Repositories;

public class SiteRepository(AnalyticsDbContext context, ILogger<SiteRepository> logger) : ISiteRepository
{
    public async Task<(string siteKey, string writeKey, UserId? ownerId)> GenerateWriteKeyAsync(string siteKey, UserId ownerId, CancellationToken ct = default)
    {
        var newKey = GenerateWriteKey();
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        
        var site = await context.AnalyticsSites.FirstOrDefaultAsync(s => s.SiteKey == siteKey, ct);
        if (site == null)
        {
            site = new AnalyticsSite { Id = SiteId.New(), SiteKey = siteKey, Name = siteKey, OwnerId = ownerId, WriteKey = newKey, CreatedAt = DateTime.UtcNow };
            context.AnalyticsSites.Add(site);
        }
        else
        {
            site.WriteKey = newKey;
            site.OwnerId ??= ownerId;
        }

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        
        logger.LogSiteKeyAction("generated", site.SiteKey, site.OwnerId?.Value, TelemetryExtensions.MaskSecret(site.WriteKey), null);
        return (site.SiteKey, site.WriteKey!, site.OwnerId);
    }

    public async Task<(string siteKey, string writeKey)?> RotateWriteKeyAsync(string siteKey, UserId ownerId, CancellationToken ct = default)
    {
        var newKey = GenerateWriteKey();
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        
        var site = await context.AnalyticsSites.FirstOrDefaultAsync(s => s.SiteKey == siteKey && s.OwnerId == ownerId, ct);
        if (site is null) return null;
        
        site.WriteKey = newKey;

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        
        logger.LogSiteKeyAction("rotated", site.SiteKey, ownerId.Value, TelemetryExtensions.MaskSecret(newKey), null);
        return (site.SiteKey, newKey);
    }

    public async Task<string?> RevokeWriteKeyAsync(string siteKey, UserId ownerId, CancellationToken ct = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        
        var site = await context.AnalyticsSites.FirstOrDefaultAsync(s => s.SiteKey == siteKey && s.OwnerId == ownerId, ct);
        if (site is null) return null;
        
        site.WriteKey = null;

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        
        logger.LogSiteKeyAction("revoked", site.SiteKey, ownerId.Value, null, null);
        return site.SiteKey;
    }

    public async Task<AnalyticsSite?> GetSiteByKeyAsync(string siteKey, CancellationToken ct = default) => await context.AnalyticsSites.FirstOrDefaultAsync(s => s.SiteKey == siteKey, ct);
    public async Task<AnalyticsSite?> GetSiteByIdAsync(SiteId siteId, CancellationToken ct = default) => await context.AnalyticsSites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
    public async Task<AnalyticsSite> CreateSiteAsync(AnalyticsSite site, CancellationToken ct = default) { context.AnalyticsSites.Add(site); await context.SaveChangesAsync(ct); return site; }
    public async Task<bool> ExistsAsync(string siteKey, CancellationToken ct = default) => await context.AnalyticsSites.AnyAsync(s => s.SiteKey == siteKey, ct);
    public async Task<int> CountOwnedSitesAsync(UserId ownerId, CancellationToken ct = default) => await context.AnalyticsSites.CountAsync(s => s.OwnerId == ownerId && !s.IsDeleted, ct);
    public async Task<bool> DeleteAsync(UserId ownerId, string siteKey, CancellationToken ct = default)
    {
        var site = await context.AnalyticsSites.FirstOrDefaultAsync(s => s.SiteKey == siteKey && s.OwnerId == ownerId, ct);
        if (site == null) return false;
        site.IsDeleted = true;
        await context.SaveChangesAsync(ct);
        return true;
    }

    private static string GenerateWriteKey() { var bytes = new byte[32]; RandomNumberGenerator.Fill(bytes); return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('='); }
}
