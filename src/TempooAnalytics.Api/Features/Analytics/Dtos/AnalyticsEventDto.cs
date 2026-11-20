namespace TempooAnalytics.Api.Features.Analytics.Dtos;

public sealed class AnalyticsEventDto
{
    public string SiteId { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string Url { get; set; } = default!;
    public string? Title { get; set; }
    public string? Referrer { get; set; }
    public ViewportDto? Viewport { get; set; }
    public string VisitorId { get; set; } = default!;
    public string SessionId { get; set; } = default!;
    public string? UserId { get; set; }
    public string? EventName { get; set; }
    public object? Data { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
