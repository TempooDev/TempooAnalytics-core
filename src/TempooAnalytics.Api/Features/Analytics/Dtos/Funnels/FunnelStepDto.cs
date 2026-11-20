namespace TempooAnalytics.Api.Features.Analytics.Dtos.Funnels;

public class FunnelStepDto
{
    public int Order { get; set; }
    public FunnelStepType Type { get; set; }
    public string Value { get; set; } = string.Empty; // URL or Event Name
    public string? Name { get; set; } // Display name for the step
}
