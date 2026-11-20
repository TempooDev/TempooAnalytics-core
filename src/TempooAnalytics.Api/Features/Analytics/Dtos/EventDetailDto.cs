namespace TempooAnalytics.Api.Features.Analytics.Dtos;

public sealed class EventDetailDto
{
    public DateTime Timestamp { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string TrafficType { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string OS { get; set; } = string.Empty;
    public string Browser { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string VisitorId { get; set; } = string.Empty;
    public string UserAlias { get; set; } = string.Empty;
}
