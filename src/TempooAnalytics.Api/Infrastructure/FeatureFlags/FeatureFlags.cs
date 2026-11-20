namespace TempooAnalytics.Api.Infrastructure.FeatureFlags;

/// <summary>
/// Switches for core features that are deployed but not released yet. They are read from configuration
/// ("FeatureFlags" section) on every access, so they can be changed without a new deploy.
/// Every flag is off unless configured. Flags of feature modules (AI, public API) live with their module
/// and reach <c>GET /features</c> through <see cref="IFeatureFlagContributor"/>.
/// </summary>
public interface IFeatureFlags
{
    /// <summary>Plausible import: upload endpoint and imported history in the dashboard.</summary>
    bool PlausibleImport { get; }

    /// <summary>Google Analytics 4 import: Google connection endpoints and the import page card.</summary>
    bool Ga4Import { get; }

    /// <summary>In-app notifications: the /notifications endpoints, the header bell and creating notifications.</summary>
    bool Notifications { get; }

    /// <summary>Heatmaps: heatmap endpoints, the public overlay endpoint and the dashboard panel.</summary>
    bool Heatmaps { get; }
}

/// <summary>Lets a feature module add its own flags to the public <c>GET /features</c> response.</summary>
public interface IFeatureFlagContributor
{
    /// <summary>Flags to expose, keyed by their camelCase JSON name (for example "aiCredits").</summary>
    IReadOnlyDictionary<string, bool> Flags { get; }
}

public sealed class ConfigurationFeatureFlags(IConfiguration configuration) : IFeatureFlags
{
    public bool PlausibleImport => configuration.GetValue<bool>("FeatureFlags:PlausibleImport");
    public bool Ga4Import => configuration.GetValue<bool>("FeatureFlags:Ga4Import");
    public bool Notifications => configuration.GetValue<bool>("FeatureFlags:Notifications");
    public bool Heatmaps => configuration.GetValue<bool>("FeatureFlags:Heatmaps");
}

public static class FeatureFlagEndpoints
{
    /// <summary>The body of <c>GET /features</c>: core flags plus those of every contributor, as camelCase booleans.</summary>
    public static Dictionary<string, bool> BuildResponse(IFeatureFlags flags, IEnumerable<IFeatureFlagContributor> contributors)
    {
        var result = new Dictionary<string, bool>
        {
            ["plausibleImport"] = flags.PlausibleImport,
            ["ga4Import"] = flags.Ga4Import,
            ["notifications"] = flags.Notifications,
            ["heatmaps"] = flags.Heatmaps,
        };
        foreach (var contributor in contributors)
            foreach (var (name, value) in contributor.Flags)
                result[name] = value;
        return result;
    }

    public static void MapFeatureFlagEndpoints(this IEndpointRouteBuilder app)
    {
        // Public so the app can decide what to show before (and after) sign-in. Only exposes booleans.
        app.MapGet("/features", (IFeatureFlags flags, IEnumerable<IFeatureFlagContributor> contributors) => Results.Ok(BuildResponse(flags, contributors)))
            .AllowAnonymous()
            .WithTags("System")
            .WithName("GetFeatureFlags");
    }
}
