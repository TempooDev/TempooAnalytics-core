namespace TempooAnalytics.Api.Features.Analytics.Dtos.Funnels;

public class FunnelStepResultDto
{
    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Sessions { get; set; }
    public double DropOffRate { get; set; }
    public double ConversionRate { get; set; } // Conversion from start of funnel
}
