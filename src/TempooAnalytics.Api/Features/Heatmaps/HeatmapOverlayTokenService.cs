using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Heatmaps;

public record HeatmapOverlayGrant(SiteId SiteId, string Path, DateTimeOffset ExpiresAt);

public interface IHeatmapOverlayTokenService
{
    (string Token, DateTimeOffset ExpiresAt) Issue(SiteId siteId, string path);
    /// <summary>The grant, or null when the token was tampered with or has expired.</summary>
    HeatmapOverlayGrant? Read(string token);
}

/// <summary>
/// Read-only token for the overlay mode: one site and one path for 10 minutes. It travels in the URL hash
/// of the customer's page, so it never reaches their server logs.
/// </summary>
public sealed class HeatmapOverlayTokenService(IDataProtectionProvider dataProtection, TimeProvider timeProvider) : IHeatmapOverlayTokenService
{
    public const string Purpose = "heatmap-overlay";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly IDataProtector _protector = dataProtection.CreateProtector(Purpose);

    private sealed record Payload(Guid SiteId, string Path, long ExpiresAt);

    public (string Token, DateTimeOffset ExpiresAt) Issue(SiteId siteId, string path)
    {
        var expiresAt = timeProvider.GetUtcNow().Add(Lifetime);
        var json = JsonSerializer.Serialize(new Payload(siteId.Value, path, expiresAt.ToUnixTimeSeconds()));
        return (_protector.Protect(json), expiresAt);
    }

    public HeatmapOverlayGrant? Read(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(token));
            if (payload is null) return null;
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAt);
            if (expiresAt <= timeProvider.GetUtcNow()) return null;
            return new HeatmapOverlayGrant(new SiteId(payload.SiteId), payload.Path, expiresAt);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
