using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TempooAnalytics.Api.Features.Analytics.Models;

[Table("admin_write_key_audit")]
public class AdminWriteKeyAudit
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Required]
    [Column("site_key")]
    public string SiteKey { get; set; } = "";

    [Required]
    [Column("admin_user")]
    public string AdminUser { get; set; } = "";

    [Required]
    [Column("action")]
    public string Action { get; set; } = "";

    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
