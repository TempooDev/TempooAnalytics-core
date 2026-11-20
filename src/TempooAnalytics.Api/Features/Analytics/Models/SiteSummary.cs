namespace TempooAnalytics.Api.Features.Analytics.Models;

public sealed class SiteSummary
{
    public Guid SiteId { get; set; }
    public string SiteKey { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? WriteKey { get; set; }
    public long TotalEvents { get; set; }
    public long Pageviews { get; set; }
    public long Visitors { get; set; }
    public DateTimeOffset? LastEventAt { get; set; }
    public string[]? AllowedOrigins { get; set; }
    public string? PublicShareToken { get; set; }
    public double BounceRate { get; set; }
}
