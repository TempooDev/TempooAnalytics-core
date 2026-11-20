namespace TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;

public sealed class MetricCardDto
{
    public long Pageviews30 { get; set; }
    public long Visitors30 { get; set; }
    public long Sessions30 { get; set; }
    public long Events30 { get; set; }
    public long CustomEvents30 { get; set; }
    public double BounceRate30 { get; set; }
    public double PagesPerSession30 { get; set; }
    public double ConversionRate30 { get; set; }
    public double AvgSessionDuration30 { get; set; }
    public double AvgPageDuration30 { get; set; }
}
