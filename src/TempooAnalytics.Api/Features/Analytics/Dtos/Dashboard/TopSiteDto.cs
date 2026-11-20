using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;

public sealed class TopSiteDto
{
    public Guid SiteId { get; set; }
    public string SiteKey { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Domain { get; set; } = string.Empty;
    public long Pageviews { get; set; }
    public long Visitors { get; set; }
    public long Events { get; set; }
    public double Conversion { get; set; }
    public double AvgSessionDuration { get; set; }
    public double AvgPageDuration { get; set; }
    public bool Active { get; set; }
}
