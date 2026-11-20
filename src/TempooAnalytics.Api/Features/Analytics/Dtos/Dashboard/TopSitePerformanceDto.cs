namespace TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;

public class TopSitePerformanceDto
{
    public Guid SiteId { get; set; }
    public long Pageviews { get; set; }
    public long Visitors { get; set; }
    public long Events { get; set; }
}
