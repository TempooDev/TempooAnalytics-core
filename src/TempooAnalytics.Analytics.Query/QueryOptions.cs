namespace TempooAnalytics.Analytics.Query;

/// <summary>
/// Per-call switches that each service decides from its own configuration, so the queries never read
/// feature flags themselves.
/// </summary>
/// <param name="IncludeImportedData">Merge imported history (Plausible, GA4) into the live totals.</param>
public sealed record QueryOptions(bool IncludeImportedData)
{
    public static readonly QueryOptions LiveOnly = new(false);
}
