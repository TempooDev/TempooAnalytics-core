namespace TempooAnalytics.Api.Features.Analytics.Dtos.Funnels;

public class FunnelDefinitionDto
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public List<FunnelStepDto> Steps { get; set; } = new();
    
    // Summary metrics for Dashboard
    public double ConversionRate { get; set; }
    public long TotalSessions { get; set; }
}
