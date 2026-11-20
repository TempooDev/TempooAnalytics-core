using TempooAnalytics.Api.Infrastructure.FeatureFlags;
using TempooAnalytics.Api.Infrastructure.Modules;

namespace TempooAnalytics.Api.Infrastructure.Services;

/// <summary>
/// Whether this server has the S3-compatible object storage that file-based features need: the Plausible and Google Analytics 4
/// imports and the CSV export all hand files to the worker through it. When it is not available those features answer
/// 501 and the app hides them, instead of failing with a 500 or leaving a job running for ever.
/// </summary>
public interface IStorageAvailability
{
    bool IsAvailable { get; }
}

public sealed class StorageAvailability(IConfiguration configuration, IServiceProvider services) : IStorageAvailability
{
    private readonly Lazy<bool> _available = new(() => Evaluate(configuration, Edition.IsCommunity(services)));

    public bool IsAvailable => _available.Value;

    /// <summary>
    /// The hosted service always runs with storage (the storage service refuses to start without credentials there, as always).
    /// Community has it when the four <c>Storage:R2:*</c> settings are present, or in development/test hosts, where the storage
    /// service falls back to a local MinIO exactly as before.
    /// </summary>
    public static bool Evaluate(IConfiguration configuration, bool community)
    {
        if (!community) return true;
        return R2Settings.HasCredentials(configuration) || R2Settings.AllowsLocalFallback(configuration);
    }

    /// <summary>Name of the flag in <c>GET /features</c>.</summary>
    public const string FlagName = "dataStorageUnavailable";
}

/// <summary>Adds <c>dataStorageUnavailable: true</c> to <c>GET /features</c> only when storage is missing, so every other host's response is unchanged.</summary>
public sealed class StorageFeatureFlagContributor(IStorageAvailability storage) : IFeatureFlagContributor
{
    public IReadOnlyDictionary<string, bool> Flags => storage.IsAvailable
        ? new Dictionary<string, bool>()
        : new Dictionary<string, bool> { [StorageAvailability.FlagName] = true };
}

/// <summary>What the storage service and its availability check both read: one definition of "configured".</summary>
public static class R2Settings
{
    public static bool HasCredentials(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Storage:R2:AccessKey"])
        && !string.IsNullOrWhiteSpace(configuration["Storage:R2:SecretKey"])
        && !string.IsNullOrWhiteSpace(configuration["Storage:R2:Endpoint"]);

    /// <summary>Development, the test host and CI may run without credentials: the storage service then talks to a local MinIO.</summary>
    public static bool AllowsLocalFallback(IConfiguration configuration)
    {
        var env = configuration["ASPNETCORE_ENVIRONMENT"];
        var isCi = !string.IsNullOrEmpty(configuration["CI"]) || !string.IsNullOrEmpty(configuration["GITHUB_ACTIONS"]);
        return env == "Development" || env == "Testing" || configuration["TEST_EXECUTION"] == "true" || isCi;
    }
}
