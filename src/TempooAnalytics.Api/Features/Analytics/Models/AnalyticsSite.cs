using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Models;

[Table("analytics_sites")]
public class AnalyticsSite
{
    [Key]
    [Column("id")]
    public SiteId Id { get; set; }

    [Required]
    [Column("site_key")]
    public string SiteKey { get; set; } = "";

    [Required]
    [Column("name")]
    public string Name { get; set; } = "";

    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("owner_id")]
    public UserId? OwnerId { get; set; }

    [Column("write_key")]
    public string? WriteKey { get; set; }

    [Column("allowed_origins")]
    public string[]? AllowedOrigins { get; set; }

    [Column("public_share_token")]
    public string? PublicShareToken { get; set; }

    [Column("is_deleted")]
    public bool IsDeleted { get; set; }
}
