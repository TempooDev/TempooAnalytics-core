namespace TempooAnalytics.Api.Features.Analytics.Dtos;

public sealed class EventSummaryDto
{
    public string EventName { get; set; } = string.Empty;
    public long Count { get; set; }

    public EventSummaryDto() { }

    public EventSummaryDto(string eventName, long count)
    {
        EventName = eventName;
        Count = count;
    }
}
