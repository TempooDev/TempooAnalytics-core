using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Billing;

/// <summary>Self-host billing: no invoices and no portal.</summary>
public class NullBillingProvider : IBillingProvider
{
    public Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(UserId userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<InvoiceDto>>([]);

    public Task<string?> GetPortalUrlAsync(UserId userId, CancellationToken ct) => Task.FromResult<string?>(null);
}
