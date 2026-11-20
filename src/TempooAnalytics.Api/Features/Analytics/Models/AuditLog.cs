using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace TempooAnalytics.Api.Features.Analytics.Models;

[Table("audit_logs")]
public class AuditLog
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Required]
    [Column("actor_id")]
    public string ActorId { get; set; } = "system";

    [Required]
    [Column("entity_id")]
    public string EntityId { get; set; } = "";

    [Required]
    [Column("entity_type")]
    public string EntityType { get; set; } = "";

    [Required]
    [Column("action")]
    public string Action { get; set; } = "";

    [Column("details", TypeName = "jsonb")]
    public JsonDocument? Details { get; set; }

    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
