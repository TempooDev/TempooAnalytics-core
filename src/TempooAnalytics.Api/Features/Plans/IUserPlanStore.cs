using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Plans;

/// <summary>The plan a user is on, as far as the core needs to know: its slug and how long their data is kept.</summary>
public sealed record UserPlan(string Slug, int RetentionMonths);

/// <summary>A plan an administrator can assign (admin console).</summary>
public sealed record PlanOption(string Slug, string DisplayName);

/// <summary>
/// Where the core reads and writes the plan of a user (subscription and tier). The core never sees the billing tables: the cloud
/// billing module implements this over them; a build without it uses <see cref="NoPlanUserStore"/> (no limits).
/// </summary>
public interface IUserPlanStore
{
    /// <summary>The plan in force for the user (their subscription's tier, "free" when they have none), or null when that plan does not exist.</summary>
    Task<UserPlan?> GetPlanAsync(UserId userId);

    /// <summary>Slug of the user's latest subscription, or null when they have none.</summary>
    Task<string?> GetSubscribedPlanAsync(UserId userId);

    /// <summary>Replaces the user's subscription with a new one on <paramref name="plan"/> (<paramref name="featuresJson"/> is the subscription's features document).</summary>
    Task SetPlanAsync(UserId userId, string plan, string featuresJson = "{}");

    /// <summary>The plans that can be assigned, smallest first.</summary>
    Task<IReadOnlyList<PlanOption>> ListPlansAsync();

    /// <summary>Removes what the store keeps for the user's plan (deleting the user). Joins the transaction the caller opened on the core context, if any.</summary>
    Task RemoveUserPlanAsync(UserId userId);
}

/// <summary>
/// Self-host store: no subscriptions to read or write, and no limits. Every user is on the "community" plan, which the
/// plan checks (import, export) treat as a plan that includes everything. Retention is not read from here in community:
/// <c>RetentionMaintenanceWorker</c> keeps data forever unless <c>Retention:Months</c> is set.
/// </summary>
public class NoPlanUserStore : IUserPlanStore
{
    public const string CommunitySlug = "community";

    /// <summary>Placeholder for "kept forever" (100 years); nothing in community reads it, it only has to be a safe value.</summary>
    private const int UnlimitedRetentionMonths = 1200;

    public Task<UserPlan?> GetPlanAsync(UserId userId) => Task.FromResult<UserPlan?>(new UserPlan(CommunitySlug, UnlimitedRetentionMonths));
    public Task<string?> GetSubscribedPlanAsync(UserId userId) => Task.FromResult<string?>(CommunitySlug);
    public Task SetPlanAsync(UserId userId, string plan, string featuresJson = "{}") => Task.CompletedTask;
    public Task<IReadOnlyList<PlanOption>> ListPlansAsync() => Task.FromResult<IReadOnlyList<PlanOption>>([]);
    public Task RemoveUserPlanAsync(UserId userId) => Task.CompletedTask;
}
