using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Models;

public enum ExportStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

[Table("export_jobs")]
public class ExportJob
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public SiteId SiteId { get; set; }

    [Required]
    public UserId UserId { get; set; }

    [Required]
    public ExportStatus Status { get; set; } = ExportStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    
    public string? ErrorMessage { get; set; }
    public string? DownloadUrl { get; set; }
}
