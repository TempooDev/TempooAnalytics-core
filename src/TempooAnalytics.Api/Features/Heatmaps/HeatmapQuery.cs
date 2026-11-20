namespace TempooAnalytics.Api.Features.Heatmaps;

public static class HeatmapDevices
{
    public const string Desktop = "desktop";
    public const string Tablet = "tablet";
    public const string Mobile = "mobile";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Desktop, Tablet, Mobile };

    /// <summary>Same viewport thresholds as the ingestion worker uses for heatmap_clicks.device_type.</summary>
    public static (int MinWidth, int MaxWidth) WidthRange(string device) => device switch
    {
        Mobile => (1, 767),
        Tablet => (768, 1023),
        _ => (1024, 65535)
    };
}

public static class HeatmapGrid
{
    public const int CellX = 10;
    public const int CellY = 20;
    public const int MaxCells = 5000;
    public const int MaxElements = 20;
    public const int MaxPages = 100;
    public const int DefaultDays = 30;

    /// <summary>
    /// Turns a histogram of leave events by scroll depth into the share of page views that reached
    /// 10 %, 20 % … 100 %. A view at 47 % counts for 10 % to 40 %.
    /// </summary>
    public static IReadOnlyList<ScrollDepthDto> ScrollDistribution(IEnumerable<(int Depth, long Views)> histogram)
    {
        var rows = histogram.ToList();
        var total = rows.Sum(r => r.Views);
        return Enumerable.Range(1, 10)
            .Select(i => i * 10)
            .Select(depth => new ScrollDepthDto(depth, total == 0
                ? 0
                : Math.Round(rows.Where(r => r.Depth >= depth).Sum(r => r.Views) * 100.0 / total, 1)))
            .ToList();
    }

    public static (DateTime Start, DateTime End) Range(DateTime? from, DateTime? to, DateTime utcNow)
    {
        if (from.HasValue && to.HasValue) return (from.Value.ToUniversalTime(), to.Value.ToUniversalTime());
        var end = utcNow.Date.AddDays(1);
        return (end.AddDays(-DefaultDays), end);
    }
}
