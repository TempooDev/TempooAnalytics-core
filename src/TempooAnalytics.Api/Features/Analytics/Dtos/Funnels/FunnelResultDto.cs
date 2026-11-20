namespace TempooAnalytics.Api.Features.Analytics.Dtos.Funnels;

public class FunnelResultDto
{
    public List<FunnelStepResultDto> Steps { get; set; } = new();
    public double OverallConversionRate { get; set; }
}
