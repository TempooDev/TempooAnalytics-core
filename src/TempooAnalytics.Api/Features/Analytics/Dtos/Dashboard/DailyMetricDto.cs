namespace TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;

public sealed class DailyMetricDto
{
    public DateTime Date { get; set; }
    public long Pageviews { get; set; }
    public long Visitors { get; set; }
    public long Events { get; set; }
    public double AvgSessionDuration { get; set; }
    public double AvgPageDuration { get; set; }
}
