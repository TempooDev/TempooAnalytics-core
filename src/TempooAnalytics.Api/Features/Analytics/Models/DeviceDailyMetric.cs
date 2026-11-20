using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Models;

[Table("device_daily_metrics", Schema = "analytics_aggregates")]
[PrimaryKey(nameof(SiteId), nameof(DeviceType), nameof(Day))]
public class DeviceDailyMetric
{
    [Column("site_id")]
    public SiteId SiteId { get; set; }

    [Column("device_type")]
    public string DeviceType { get; set; } = string.Empty;

    [Column("day")]
    public DateTime Day { get; set; }

    [Column("value")]
    public long Value { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
