using System.Text;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Services;

public class ExportService(IMetricsRepository metricsRepository) : IExportService
{
    public async Task<string> ExportDailyMetricsToCsvAsync(UserId userId, string siteKey, DateTime start, DateTime end, CancellationToken ct = default)
    {
        var metrics = await metricsRepository.GetDailyMetricsAsync(userId, siteKey, start, end, ct);
        
        var csv = new StringBuilder();
        csv.AppendLine("Date,Visitors,Pageviews,Events,AvgSessionDurationSeconds");

        foreach (var metric in metrics)
        {
            csv.AppendLine($"{metric.Day:yyyy-MM-dd},{metric.Visitors},{metric.Pageviews},{metric.Events},{metric.AvgSessionDurationSeconds:F2}");
        }

        return csv.ToString();
    }
}
