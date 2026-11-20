using TempooAnalytics.Api.Infrastructure.Modules;

namespace TempooAnalytics.Api.Features.Billing;

/// <summary>
/// Marks a feature module that reads or writes the billing tables. Since slice c.6 the core never touches <c>billing.*</c> itself
/// (the tiers are seeded by the cloud's <see cref="Infrastructure.Modules.IModuleStartup"/> step); the marker only says what the module is.
/// </summary>
public interface IBillingModule : IFeatureModule { }

/// <summary>
/// Marks the module that serves the cloud billing routes. <see cref="Infrastructure.Modules.Edition"/> derives "cloud" from it,
/// so the core does not need to name the module that implements it.
/// </summary>
public interface IBillingEndpointsModule : IBillingModule { }
