namespace TempooAnalytics.Api.Features.Analytics.Dtos;

public sealed class GroupedEventsQueryDto
{
    public string? EventName { get; set; }
    public string? Keys { get; set; }
    public int? Days { get; set; }
}
