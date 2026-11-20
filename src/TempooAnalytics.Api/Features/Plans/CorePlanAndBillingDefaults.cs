using Microsoft.Extensions.DependencyInjection.Extensions;
using TempooAnalytics.Api.Features.Billing;

namespace TempooAnalytics.Api.Features.Plans;

public static class CorePlanAndBillingDefaults
{
    /// <summary>
    /// What the core runs with when no module says otherwise: no plan limits and no billing. The cloud module
    /// (the payments module) replaces both with the tier policy and the payments provider.
    /// </summary>
    public static IServiceCollection AddCorePlanAndBillingDefaults(this IServiceCollection services)
    {
        services.TryAddScoped<IPlanPolicy, CommunityPlanPolicy>();
        services.TryAddScoped<IBillingProvider, NullBillingProvider>();
        services.TryAddScoped<IUserPlanStore, NoPlanUserStore>();
        return services;
    }
}
