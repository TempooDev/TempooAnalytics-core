using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Data;

namespace TempooAnalytics.Api.Features.Notifications;

/// <summary>Notifications are deleted 90 days after they were created, read or not.</summary>
public static class NotificationRetention
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(90);
    public const int BatchSize = 5000;

    /// <returns>Number of notifications deleted.</returns>
    public static async Task<int> DeleteExpiredAsync(AnalyticsDbContext db, DateTime now, CancellationToken ct = default)
    {
        var cutoff = now - MaxAge;
        var total = 0;
        while (!ct.IsCancellationRequested)
        {
            // Small batches keep each delete short.
            var deleted = await db.Notifications
                .Where(n => db.Notifications
                    .Where(old => old.CreatedAt < cutoff)
                    .Select(old => old.Id)
                    .Take(BatchSize)
                    .Contains(n.Id))
                .ExecuteDeleteAsync(ct);
            total += deleted;
            if (deleted < BatchSize) break;
        }
        return total;
    }
}
