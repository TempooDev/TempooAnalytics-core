namespace TempooAnalytics.Api.Features.Billing;

/// <summary>
/// Extension point behind the "Sync with the provider" action of the tiers admin page: pulls the plan catalog from the
/// payment provider into the local tiers. The cloud payments module registers an implementation (only when payments
/// are enabled); a build without it has none and the page does nothing for that action.
/// </summary>
public interface ITierCatalogSync
{
    /// <summary>Runs the sync. Throws if the provider cannot be reached; a provider-side failure comes back as an unsuccessful result.</summary>
    Task<TierCatalogSyncResult> SyncAsync(CancellationToken ct = default);
}

/// <summary>Outcome of <see cref="ITierCatalogSync.SyncAsync"/>.</summary>
public sealed record TierCatalogSyncResult(bool Success, string? Message, int Updated, int Created, int Benefits, int Meters);
