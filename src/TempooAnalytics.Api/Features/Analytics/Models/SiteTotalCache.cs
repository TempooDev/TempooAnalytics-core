using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Models;

[Table("site_totals_cache", Schema = "analytics_aggregates")]
[PrimaryKey(nameof(SiteId), nameof(PeriodStart), nameof(Days))]
public class SiteTotalCache
{
    [Column("site_id")]
    public SiteId SiteId { get; set; }

    [Column("period_start")]
    public DateTime PeriodStart { get; set; }

    [Column("days")]
    public int Days { get; set; }

    [Column("pageviews")]
    public long Pageviews { get; set; }

    [Column("visitors")]
    public long Visitors { get; set; }

    [Column("events")]
    public long Events { get; set; }

    [Column("conversion_rate")]
    public double ConversionRate { get; set; }

    [Column("avg_session_duration_seconds")]
    public double AvgSessionDurationSeconds { get; set; }

    [Column("avg_page_duration_seconds")]
    public double AvgPageDurationSeconds { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
