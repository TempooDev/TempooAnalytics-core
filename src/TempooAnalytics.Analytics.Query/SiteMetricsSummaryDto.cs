namespace TempooAnalytics.Analytics.Query;

public class SiteMetricsSummaryDto
{
    public long Pageviews { get; set; }
    public long Visitors { get; set; }
    public long Sessions { get; set; }
    public long BouncedSessions { get; set; }
    public long TotalEvents { get; set; }
    public long CustomEvents { get; set; }
    public double AvgSessionDuration { get; set; }
    public double BounceRate { get; set; }

    /// <summary>The totals include imported history (days before the site's first live event).</summary>
    public bool IncludesImportedData { get; set; }

    /// <summary>The range has imported history, but it was left out because filters are applied.</summary>
    public bool ImportedDataExcludedByFilter { get; set; }
}
