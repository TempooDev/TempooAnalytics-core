using TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;

using TempooAnalytics.Api.Features.Analytics.Dtos.Funnels;

namespace TempooAnalytics.Api.Features.Analytics.Dtos;

public class PublicPreviewResponse
{
    public string SiteName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public IEnumerable<PublicMetricDto> Data { get; set; } = new List<PublicMetricDto>();
    public IEnumerable<PublicTopPageDto> TopPages { get; set; } = new List<PublicTopPageDto>();
    public IReadOnlyList<HourlyActivityDto>? HourlyActivity { get; set; }
    public IReadOnlyList<DeviceDto>? Devices { get; set; }
    public SiteMetricsSummaryDto? Summary { get; set; }
    public IReadOnlyList<LocationDto>? Locations { get; set; }
    public IReadOnlyList<FunnelDefinitionDto>? Funnels { get; set; }
    public SiteMetricsSummaryDto? Metrics30Previous { get; set; }
}


public class PublicMetricDto
{
    public DateTime Date { get; set; }
    public long Visitors { get; set; }
    public long Pageviews { get; set; }
    public long Events { get; set; }
    public double AvgSessionDuration { get; set; }
}

public class PublicTopPageDto
{
    public string Url { get; set; } = string.Empty;
    public long Views { get; set; }
    public double? AvgTimeOnPage { get; set; }
}
