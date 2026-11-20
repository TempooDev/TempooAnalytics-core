namespace TempooAnalytics.Api.Shared.Telemetry;

public static class TelemetryConstants
{
    // ActivitySource name used across the API for traces
    public const string ActivitySourceName = "TempooAnalytics.Api";

    // Common attribute keys
    public const string TagUserId = "user.id";
    public const string TagUserName = "user.name";
    public const string TagSiteId = "site.id";
    public const string TagAction = "action";
    public const string TagResult = "result";
    public const string TagClientIp = "client.ip";
}
