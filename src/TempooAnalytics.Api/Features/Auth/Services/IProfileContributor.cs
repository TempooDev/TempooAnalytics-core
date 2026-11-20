using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Auth.Services;

/// <summary>
/// Lets a module add fields to <c>GET /api/auth/me</c> without the core knowing about them. The core registers none:
/// a community build returns identity only.
/// </summary>
public interface IProfileContributor
{
    /// <summary>Adds top-level fields (camelCase keys) to <paramref name="fields"/>. Must not throw for ordinary failures.</summary>
    Task ContributeAsync(UserId userId, IDictionary<string, object?> fields, CancellationToken ct);
}
