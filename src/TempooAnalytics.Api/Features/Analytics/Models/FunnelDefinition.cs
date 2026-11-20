using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using TempooAnalytics.Api.Features.Analytics.Dtos.Funnels;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Models;

[Table("analytics_funnels")]
public class FunnelDefinition
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("site_id")]
    public SiteId SiteId { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("steps", TypeName = "jsonb")]
    public string StepsJson { get; set; } = "[]";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [NotMapped]
    public List<FunnelStepDto> Steps
    {
        get => string.IsNullOrEmpty(StepsJson)
            ? new List<FunnelStepDto>()
            : JsonSerializer.Deserialize<List<FunnelStepDto>>(StepsJson) ?? new List<FunnelStepDto>();
        set => StepsJson = JsonSerializer.Serialize(value);
    }
}
