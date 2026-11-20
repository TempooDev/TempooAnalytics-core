using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Plans;

/// <summary>Self-host policy: no plans, no limits. Not registered by default in the cloud build.</summary>
public class CommunityPlanPolicy : IPlanPolicy
{
    public Task<int?> MaxSitesAsync(UserId userId, CancellationToken ct) => Task.FromResult<int?>(null);
    public Task<bool> HasAdvancedAnalyticsAsync(UserId userId, CancellationToken ct) => Task.FromResult(true);
    public Task<bool> CanCreateSiteAsync(UserId userId, CancellationToken ct) => Task.FromResult(true);
}
