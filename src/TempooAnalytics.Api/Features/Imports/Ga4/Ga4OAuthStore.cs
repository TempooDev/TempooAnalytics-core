using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;

namespace TempooAnalytics.Api.Features.Imports.Ga4;

/// <summary>Authorization started for a user and site, waiting for Google to send the user back.</summary>
public sealed record Ga4StateRecord(Guid UserId, Guid SiteId, string CodeVerifier, DateTimeOffset CreatedAt);

/// <summary>Google access granted, waiting for the user to choose one of the listed properties.</summary>
public sealed record Ga4ConnectionRecord(Guid UserId, Guid SiteId, string AccessToken, string[] PropertyIds, DateTimeOffset ExpiresAt);

/// <summary>
/// Short-lived, single-use OAuth records. Values are encrypted with DataProtection before they reach the
/// distributed cache (Redis in production), so the Google token is never stored in clear text, and every
/// read removes the record.
/// </summary>
public interface IGa4OAuthStore
{
    Task SaveStateAsync(string state, Ga4StateRecord record, CancellationToken ct = default);
    Task<Ga4StateRecord?> TakeStateAsync(string state, CancellationToken ct = default);
    Task SaveConnectionAsync(string connectionId, Ga4ConnectionRecord record, CancellationToken ct = default);
    Task<Ga4ConnectionRecord?> TakeConnectionAsync(string connectionId, CancellationToken ct = default);
}

public sealed class Ga4OAuthStore(IDistributedCache cache, IDataProtectionProvider dataProtection, TimeProvider timeProvider) : IGa4OAuthStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private const string Purpose = "Ga4Import.OAuth";

    private readonly IDataProtector _protector = dataProtection.CreateProtector(Purpose);

    public Task SaveStateAsync(string state, Ga4StateRecord record, CancellationToken ct = default) =>
        SaveAsync(StateKey(state), JsonSerializer.SerializeToUtf8Bytes(record, Ga4JsonContext.Default.Ga4StateRecord), record.CreatedAt + Lifetime, ct);

    public async Task<Ga4StateRecord?> TakeStateAsync(string state, CancellationToken ct = default)
    {
        var json = await TakeAsync(StateKey(state), ct);
        if (json == null) return null;
        var record = JsonSerializer.Deserialize(json, Ga4JsonContext.Default.Ga4StateRecord);
        return record != null && timeProvider.GetUtcNow() < record.CreatedAt + Lifetime ? record : null;
    }

    public Task SaveConnectionAsync(string connectionId, Ga4ConnectionRecord record, CancellationToken ct = default)
    {
        // Never longer than 10 minutes, and never longer than the token itself.
        var expiresAt = Min(record.ExpiresAt, timeProvider.GetUtcNow() + Lifetime);
        return SaveAsync(ConnectionKey(connectionId), JsonSerializer.SerializeToUtf8Bytes(record with { ExpiresAt = expiresAt }, Ga4JsonContext.Default.Ga4ConnectionRecord), expiresAt, ct);
    }

    public async Task<Ga4ConnectionRecord?> TakeConnectionAsync(string connectionId, CancellationToken ct = default)
    {
        var json = await TakeAsync(ConnectionKey(connectionId), ct);
        if (json == null) return null;
        var record = JsonSerializer.Deserialize(json, Ga4JsonContext.Default.Ga4ConnectionRecord);
        return record != null && timeProvider.GetUtcNow() < record.ExpiresAt ? record : null;
    }

    private static string StateKey(string state) => $"ga4:state:{state}";
    private static string ConnectionKey(string connectionId) => $"ga4:conn:{connectionId}";

    private Task SaveAsync(string key, byte[] json, DateTimeOffset expiresAt, CancellationToken ct)
    {
        // Relative, so the cache's own clock applies; reads also check expiresAt against our TimeProvider.
        var ttl = expiresAt - timeProvider.GetUtcNow();
        if (ttl <= TimeSpan.Zero) return Task.CompletedTask;
        return cache.SetAsync(key, _protector.Protect(json), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
    }

    private async Task<byte[]?> TakeAsync(string key, CancellationToken ct)
    {
        // Keys come from the browser: reject anything that can't be one of ours before touching the cache.
        if (key.Length > 128) return null;

        var protectedValue = await cache.GetAsync(key, ct);
        if (protectedValue == null) return null;
        await cache.RemoveAsync(key, ct);

        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Ga4StateRecord))]
[JsonSerializable(typeof(Ga4ConnectionRecord))]
[JsonSerializable(typeof(GoogleTokenResponse))]
[JsonSerializable(typeof(GoogleErrorResponse))]
[JsonSerializable(typeof(AccountSummariesResponse))]
[JsonSerializable(typeof(PropertyResponse))]
[JsonSerializable(typeof(RunReportRequest))]
[JsonSerializable(typeof(RunReportResponse))]
internal partial class Ga4JsonContext : JsonSerializerContext
{
}
