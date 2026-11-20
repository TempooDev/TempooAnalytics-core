using System.Globalization;
using TempooAnalytics.Api.Features.Imports.Models;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Notifications;

public sealed record NotificationText(string Title, string? Body);

/// <summary>
/// Title and text of each notification type, in English like the rest of the app. They are stored with the
/// notification, so a new type doesn't need an app deploy and old notifications keep their original text.
/// </summary>
public static class NotificationTexts
{
    public static NotificationText ImportCompleted(string siteName, ImportType source, long rows) => new(
        "Import completed",
        $"Your {SourceName(source)} history for {siteName} was imported ({rows.ToString("N0", CultureInfo.InvariantCulture)} rows).");

    public static NotificationText ImportFailed(string siteName, ImportType source, string? errorMessage) => new(
        "Import failed",
        string.IsNullOrWhiteSpace(errorMessage)
            ? $"The {SourceName(source)} import for {siteName} failed."
            : $"The {SourceName(source)} import for {siteName} failed: {errorMessage}");

    public static NotificationText ExportCompleted(string siteName) => new(
        "Export ready",
        $"Your CSV export for {siteName} is ready. We sent the download link to your email.");

    /// <summary>
    /// Without the job's error: it is the worker's raw error (storage hosts, buckets, request ids) and stays in
    /// the logs and export_jobs.error_message. Import errors are shown because they explain the user's file.
    /// </summary>
    public static NotificationText ExportFailed(string siteName) => new(
        "Export failed",
        $"The CSV export for {siteName} failed. Please try again.");

    public static NotificationText ApiQuotaWarning(int percent, long used, int limit) => new(
        "API usage at " + percent + "%",
        $"You have used {used.ToString("N0", CultureInfo.InvariantCulture)} of your {limit.ToString("N0", CultureInfo.InvariantCulture)} public API units this month. The quota renews on the 1st.");

    public static NotificationText ApiQuotaExhausted(int limit) => new(
        "API quota reached",
        $"You have used all {limit.ToString("N0", CultureInfo.InvariantCulture)} public API units for this month. Calls will return 429 until the quota renews on the 1st.");

    public const string ApiUsageLink = "/account/profile";

    public static string ImportsLink(SiteId siteId) => $"/account/sites/{siteId.Value}/imports";

    /// <summary>Not the download URL: it expires after 24 hours and is only sent by email.</summary>
    public const string DashboardLink = "/";

    public static string SourceName(ImportType source) => source switch
    {
        ImportType.GoogleAnalytics4 => "Google Analytics",
        ImportType.Plausible => "Plausible",
        _ => "CSV"
    };
}
