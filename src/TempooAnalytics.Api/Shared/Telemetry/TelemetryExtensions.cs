using System.Diagnostics;

namespace TempooAnalytics.Api.Shared.Telemetry;

public static class TelemetryExtensions
{
    private static readonly ActivitySource _source = new ActivitySource(TelemetryConstants.ActivitySourceName);

    public static Activity? StartActivity(string name)
    {
        try
        {
            return _source.StartActivity(name, ActivityKind.Server);
        }
        catch
        {
            return null;
        }
    }

    public static void SetTagSafe(this Activity? activity, string key, object? value)
    {
        if (activity is null) return;
        if (value is null) return;
        try { activity.SetTag(key, value); } catch { }
    }

    public static string? ClientIp(this HttpContext? ctx)
    {
        try
        {
            return ctx?.Connection?.RemoteIpAddress?.ToString();
        }
        catch
        {
            return null;
        }
    }

    public static string MaskSecret(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
        if (s.Length <= 8) return new string('*', s.Length);
        return s.Substring(0, 4) + "..." + s.Substring(s.Length - 4);
    }

    public static void LogAuth(this ILogger logger, string action, Guid? userId, string? username, bool success, HttpContext? ctx = null)
    {
        using var a = StartActivity($"auth.{action}");
        a?.SetTag(TelemetryConstants.TagAction, action);
        if (userId.HasValue) a?.SetTag(TelemetryConstants.TagUserId, userId.Value);
        if (!string.IsNullOrWhiteSpace(username)) a?.SetTag(TelemetryConstants.TagUserName, username);
        a?.SetTag(TelemetryConstants.TagResult, success ? "success" : "failure");
        a?.SetTag(TelemetryConstants.TagClientIp, ctx.ClientIp());

        logger.LogInformation("Auth {Action} userId={UserId} user={Username} success={Success} clientIp={ClientIp}", action, userId?.ToString(), username, success, ctx.ClientIp());
    }

    public static void LogSiteKeyAction(this ILogger logger, string action, string siteKey, Guid? adminUserId, string? maskedWriteKey, HttpContext? ctx = null)
    {
        using var a = StartActivity($"site.writekey.{action}");
        a?.SetTag(TelemetryConstants.TagAction, action);
        a?.SetTag(TelemetryConstants.TagSiteId, siteKey);
        if (adminUserId.HasValue) a?.SetTag(TelemetryConstants.TagUserId, adminUserId.Value);
        if (!string.IsNullOrWhiteSpace(maskedWriteKey)) a?.SetTag("writekey.masked", maskedWriteKey);
        a?.SetTag(TelemetryConstants.TagClientIp, ctx.ClientIp());

        logger.LogInformation("Site write-key {Action} site={SiteKey} adminUser={Admin} maskedKey={Masked} clientIp={ClientIp}", action, siteKey, adminUserId?.ToString(), maskedWriteKey, ctx.ClientIp());
    }
}
