using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Notifications.Models;

/// <summary>Stored as smallint: the values must not change.</summary>
public enum NotificationSeverity : short
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

/// <summary>An entry of a user's notification inbox (table notifications.notifications).</summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public UserId UserId { get; set; }

    /// <summary>Stable slug, see <see cref="NotificationTypes"/>.</summary>
    public string Type { get; set; } = string.Empty;
    public NotificationSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }

    /// <summary>Relative route of the app, never an external URL.</summary>
    public string? Link { get; set; }
    public SiteId? SiteId { get; set; }

    /// <summary>Id of what the notification is about (import or export job). Unique per user and type.</summary>
    public Guid? SourceId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public static class NotificationTypes
{
    public const string ImportCompleted = "import.completed";
    public const string ImportFailed = "import.failed";
    public const string ExportCompleted = "export.completed";
    public const string ExportFailed = "export.failed";
    public const string ApiQuotaWarning = "api.quota_warning";
    public const string ApiQuotaExhausted = "api.quota_exhausted";
}
