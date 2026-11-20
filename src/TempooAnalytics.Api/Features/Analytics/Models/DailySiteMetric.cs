using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Models;

[Table("daily_site_metrics", Schema = "analytics_aggregates")]
[PrimaryKey(nameof(SiteId), nameof(Day))]
public class DailySiteMetric
{
    [Column("site_id")]
    public SiteId SiteId { get; set; }

    [Column("day")]
    public DateTime Day { get; set; }

    [Column("pageviews")]
    public long Pageviews { get; set; }

    [Column("visitors")]
    public long Visitors { get; set; }

    [Column("events")]
    public long Events { get; set; }

    [Column("conversions")]
    public long Conversions { get; set; }

    [Column("avg_session_duration_seconds")]
    public double AvgSessionDurationSeconds { get; set; }

    [Column("avg_page_duration_seconds")]
    public double AvgPageDurationSeconds { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
