namespace TempooAnalytics.Api.Features.Analytics.Dtos.Funnels;

public class EventMetadataDto
{
    public List<string> EventNames { get; set; } = new();
    public List<string> Urls { get; set; } = new();
}
