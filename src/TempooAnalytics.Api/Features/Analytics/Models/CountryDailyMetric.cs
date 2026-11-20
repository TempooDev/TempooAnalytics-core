using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Models;

[Table("country_daily_metrics", Schema = "analytics_aggregates")]
[PrimaryKey(nameof(SiteId), nameof(CountryCode), nameof(Day))]
public class CountryDailyMetric
{
    [Column("site_id")]
    public SiteId SiteId { get; set; }

    [Column("country_code")]
    public string CountryCode { get; set; } = "";

    [Column("day")]
    public DateTime Day { get; set; }

    [Column("value")]
    public long Value { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
