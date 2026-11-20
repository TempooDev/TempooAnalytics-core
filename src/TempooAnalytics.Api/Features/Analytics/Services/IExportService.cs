using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Services;

public interface IExportService
{
    Task<string> ExportDailyMetricsToCsvAsync(UserId userId, string siteKey, DateTime start, DateTime end, CancellationToken ct = default);
}
