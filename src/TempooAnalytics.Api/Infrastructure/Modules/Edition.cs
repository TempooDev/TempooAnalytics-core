using TempooAnalytics.Api.Features.Billing;

namespace TempooAnalytics.Api.Infrastructure.Modules;

/// <summary>
/// Which edition this host runs as. The core has no setting for it: the cloud edition is "the billing module is loaded"
/// (<see cref="IBillingEndpointsModule"/>, the same seam the billing routes use), so community is the absence of that module
/// (not shipped, or <c>Modules:Enabled=false</c>). Deriving it from the loaded modules means it cannot disagree with what the host actually serves.
/// </summary>
public static class Edition
{
    public static bool IsCommunity(IEnumerable<IFeatureModule> modules) => !modules.Any(m => m is IBillingEndpointsModule);

    /// <summary>A host that never registered the module catalog has no module loaded, so it is community.</summary>
    public static bool IsCommunity(IServiceProvider services) =>
        IsCommunity(services.GetService<FeatureModuleCatalog>()?.Modules ?? []);
}
