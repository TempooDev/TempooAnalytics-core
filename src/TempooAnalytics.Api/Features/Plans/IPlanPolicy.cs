using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Plans;

/// <summary>
/// Extension point for plan-based permissions and limits. The cloud build reads the user's
/// SubscriptionTier (<c>TierPlanPolicy</c>, in the cloud module); a self-hosted build has no plans and uses
/// <see cref="CommunityPlanPolicy"/>.
/// </summary>
public interface IPlanPolicy
{
    /// <summary>Effective site limit for the user (null = unlimited).</summary>
    Task<int?> MaxSitesAsync(UserId userId, CancellationToken ct);

    Task<bool> HasAdvancedAnalyticsAsync(UserId userId, CancellationToken ct);

    /// <summary>Whether the user may create one more site, given the sites they already own.</summary>
    Task<bool> CanCreateSiteAsync(UserId userId, CancellationToken ct);
}
