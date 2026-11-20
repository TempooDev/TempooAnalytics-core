using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using TempooAnalytics.Api.Infrastructure.Common;
using CommonId = TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Models;

[Table("analytics_events")]
public class AnalyticsEvent
{
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("site_id")]
    public SiteId SiteId { get; set; }

    [Column("id")]
    public CommonId.EventId Id { get; set; }

    [Column("type")]
    public EventType Type { get; set; }

    [Column("url")]
    public string Url { get; set; } = string.Empty;

    [Column("referrer")]
    public string? Referrer { get; set; }

    [Column("visitor_id")]
    public string VisitorId { get; set; } = string.Empty;

    [Column("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [Column("user_id")]
    public string? UserId { get; set; }

    [Column("event_name")]
    public string? EventName { get; set; }

    [Column("data", TypeName = "jsonb")]
    public JsonDocument? Data { get; set; }

    [Column("country_code")]
    public string? CountryCode { get; set; }
}
