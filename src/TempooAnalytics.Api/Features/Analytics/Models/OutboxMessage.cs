using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace TempooAnalytics.Api.Features.Analytics.Models;

[Table("outbox_messages")]
public class OutboxMessage
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("aggregate_id")]
    public Guid? AggregateId { get; set; }

    [Required]
    [Column("type")]
    public string Type { get; set; } = "";

    [Required]
    [Column("payload", TypeName = "jsonb")]
    public JsonDocument Payload { get; set; } = null!;

    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("processed_at")]
    public DateTime? ProcessedAt { get; set; }

    [Column("error")]
    public string? Error { get; set; }
}
