using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Imports.Models;

public enum ImportStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    Cancelled
}

public enum ImportType
{
    GoogleAnalytics4,
    CSV,
    Plausible
}

[Table("import_jobs")]
public class ImportJob
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public SiteId SiteId { get; set; }

    [Required]
    public ImportType Type { get; set; }

    /// <summary>User who started the import. Null for jobs created before it was stored (or a deleted user).</summary>
    public UserId? CreatedByUserId { get; set; }

    [Required]
    public ImportStatus Status { get; set; } = ImportStatus.Pending;

    public string? ExternalPropertyId { get; set; } // For GA4
    
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public long RowsProcessed { get; set; }
    public long TotalRowsExpected { get; set; }

    public string? ErrorMessage { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    
    public string? ConfigurationJson { get; set; } // Additional details (e.g. CSV column mapping)
}
