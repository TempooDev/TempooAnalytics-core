using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Billing;

/// <summary>
/// Extension point for invoices and the customer portal. The cloud build talks to the payments provider through the
/// payments gRPC service (provider registered by the payments module); a self-hosted build has no billing and
/// uses <see cref="NullBillingProvider"/>.
/// </summary>
public interface IBillingProvider
{
    /// <summary>Invoices of the user. Empty when there is no billing or nothing to show; throws if the provider fails.</summary>
    Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(UserId userId, CancellationToken ct);

    /// <summary>Customer portal URL, or null when the user has none. Throws if the provider fails.</summary>
    Task<string?> GetPortalUrlAsync(UserId userId, CancellationToken ct);
}

/// <summary>The billing provider is not reachable (for example payments are disabled in this environment).</summary>
public class BillingUnavailableException(string message) : Exception(message);
