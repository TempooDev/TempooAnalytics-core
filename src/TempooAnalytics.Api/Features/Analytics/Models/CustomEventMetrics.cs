namespace TempooAnalytics.Api.Features.Analytics.Models
{
    public sealed class CustomEventMetrics
    {
        public DateTime Date { get; set; }

        public string EventName { get; set; } = string.Empty;

        public string Data { get; set; } = string.Empty;

        public string SessionId { get; set; } = string.Empty;

        public string VisitorId { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;
    }
}
