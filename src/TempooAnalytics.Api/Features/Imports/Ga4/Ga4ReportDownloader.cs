using System.Globalization;

namespace TempooAnalytics.Api.Features.Imports.Ga4;

/// <summary>
/// Downloads the daily totals and the six breakdowns of a GA4 property with the Data API, one month at a
/// time and one request at a time (GA4 allows few concurrent requests per property), into a
/// <see cref="Ga4ExportWriter"/>.
/// </summary>
public sealed class Ga4ReportDownloader(IGoogleAnalyticsClient google)
{
    public const int PageSize = 100_000;

    /// <summary>Top pages kept per month, so a large site stays within the worker's file limits.</summary>
    public const int MaxPagesPerMonth = 10_000;

    private static readonly List<NamedItem> TotalsMetrics =
        [new("totalUsers"), new("screenPageViews"), new("sessions"), new("engagedSessions"), new("averageSessionDuration")];

    private static readonly List<NamedItem> BreakdownMetrics = [new("totalUsers"), new("screenPageViews")];

    public async Task DownloadAsync(string accessToken, string propertyId, DateOnly start, DateOnly end, Ga4ExportWriter writer, CancellationToken ct = default)
    {
        foreach (var (from, to) in Months(start, end))
        {
            var range = new List<DateRange> { new(Iso(from), Iso(to)) };

            await ForEachRowAsync(accessToken, propertyId, new RunReportRequest(range, [new("date")], TotalsMetrics, PageSize, 0, false), row =>
                writer.AddTotals(Day(row, 0), Long(row, 0), Long(row, 1), Long(row, 2), Long(row, 3), Double(row, 4)), allPages: true, ct);

            foreach (var breakdown in Ga4Breakdown.All)
            {
                var isPages = breakdown == Ga4Breakdown.Pages;
                var request = new RunReportRequest(
                    range,
                    [new("date"), new(breakdown.Dimension)],
                    BreakdownMetrics,
                    isPages ? MaxPagesPerMonth : PageSize,
                    0,
                    false,
                    isPages ? [new OrderBy(new MetricOrderBy("screenPageViews"), Desc: true)] : null);

                await ForEachRowAsync(accessToken, propertyId, request, row =>
                    writer.AddBreakdown(breakdown, Day(row, 0), Dimension(row, 1), Long(row, 0), Long(row, 1)), allPages: !isPages, ct);
            }
        }
    }

    /// <summary>Calendar months covering [start, end], clipped to the range.</summary>
    public static IEnumerable<(DateOnly From, DateOnly To)> Months(DateOnly start, DateOnly end)
    {
        for (var from = start; from <= end;)
        {
            var monthEnd = new DateOnly(from.Year, from.Month, 1).AddMonths(1).AddDays(-1);
            var to = monthEnd < end ? monthEnd : end;
            yield return (from, to);
            from = to.AddDays(1);
        }
    }

    private async Task ForEachRowAsync(string accessToken, string propertyId, RunReportRequest request, Action<ReportRow> onRow, bool allPages, CancellationToken ct)
    {
        long offset = 0;
        while (true)
        {
            var page = await google.RunReportAsync(accessToken, propertyId, request with { Offset = offset }, ct);
            var rows = page.Rows ?? [];
            foreach (var row in rows) onRow(row);

            offset += rows.Count;
            if (!allPages || rows.Count == 0 || offset >= page.RowCount) return;
        }
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly Day(ReportRow row, int i) =>
        DateOnly.ParseExact(Dimension(row, i), "yyyyMMdd", CultureInfo.InvariantCulture);

    private static string Dimension(ReportRow row, int i) => row.DimensionValues?.ElementAtOrDefault(i)?.Value ?? "";

    private static long Long(ReportRow row, int i) =>
        long.TryParse(row.MetricValues?.ElementAtOrDefault(i)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double Double(ReportRow row, int i) =>
        double.TryParse(row.MetricValues?.ElementAtOrDefault(i)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
