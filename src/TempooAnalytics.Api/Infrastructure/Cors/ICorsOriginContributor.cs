namespace TempooAnalytics.Api.Infrastructure.Cors;

/// <summary>
/// Web origins a module adds to the core's CORS policies. The core only knows its own development hosts (and the operator's
/// <c>Cors:AllowedOrigins</c>): the hosts of the hosted service come from its module, so no production host or personal domain
/// is part of the open core.
/// </summary>
public interface ICorsOriginContributor
{
    /// <summary>Added to the <c>AllowFrontend</c> policy (the app's own API calls).</summary>
    IReadOnlyCollection<string> FrontendOrigins { get; }

    /// <summary>Added to the <c>LandingOnly</c> policy (public forms of the marketing site).</summary>
    IReadOnlyCollection<string> LandingOrigins { get; }
}
