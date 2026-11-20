using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace TempooAnalytics.Api.Features.Imports.Ga4;

/// <summary>A GA4 breakdown and the Plausible export table it is written to.</summary>
public sealed record Ga4Breakdown(string Dimension, string Table, string Column)
{
    public static readonly Ga4Breakdown Pages = new("pagePath", "imported_pages", "page");
    public static readonly Ga4Breakdown Sources = new("sessionSource", "imported_sources", "source");
    public static readonly Ga4Breakdown Countries = new("countryId", "imported_locations", "country");
    public static readonly Ga4Breakdown Devices = new("deviceCategory", "imported_devices", "device");
    public static readonly Ga4Breakdown Browsers = new("browser", "imported_browsers", "browser");
    public static readonly Ga4Breakdown OperatingSystems = new("operatingSystem", "imported_operating_systems", "operating_system");

    public static readonly IReadOnlyList<Ga4Breakdown> All = [Pages, Sources, Countries, Devices, Browsers, OperatingSystems];
}

/// <summary>
/// Writes GA4 report rows in the format of Plausible's CSV export (one CSV per table, zipped), so the
/// worker imports them with the Plausible reader. Each table is buffered in its own temporary file.
/// </summary>
public sealed class Ga4ExportWriter : IAsyncDisposable
{
    private const string VisitorsTable = "imported_visitors";

    // GA4 placeholders that are not a real value. Direct traffic has no referrer in live data either.
    private static readonly HashSet<string> DiscardedValues = new(StringComparer.OrdinalIgnoreCase) { "(direct)", "(not set)", "(other)", "" };

    private readonly DateOnly _start;
    private readonly DateOnly _end;
    private readonly Dictionary<string, (string Path, StreamWriter Writer)> _tables = new();

    public long DailyRows { get; private set; }
    public long BreakdownRows { get; private set; }

    public Ga4ExportWriter(DateOnly start, DateOnly end)
    {
        _start = start;
        _end = end;
        Open(VisitorsTable, "date,visitors,pageviews,bounces,visits,visit_duration");
        foreach (var b in Ga4Breakdown.All) Open(b.Table, $"date,{b.Column},visitors,pageviews");
    }

    /// <summary>
    /// Daily totals: bounces are sessions that were not engaged, and the total visit duration is the
    /// average session duration times sessions.
    /// </summary>
    public void AddTotals(DateOnly date, long totalUsers, long views, long sessions, long engagedSessions, double averageSessionDuration)
    {
        var bounces = Math.Max(0, sessions - engagedSessions);
        var visitDuration = (long)Math.Round(averageSessionDuration * sessions, MidpointRounding.AwayFromZero);
        _tables[VisitorsTable].Writer.WriteLine(string.Join(',',
            Date(date), N(totalUsers), N(views), N(bounces), N(sessions), N(visitDuration)));
        DailyRows++;
    }

    /// <returns>false when the value is a GA4 placeholder and was not written.</returns>
    public bool AddBreakdown(Ga4Breakdown breakdown, DateOnly date, string value, long totalUsers, long views)
    {
        var v = value.Trim();
        if (DiscardedValues.Contains(v)) return false;
        _tables[breakdown.Table].Writer.WriteLine(string.Join(',', Date(date), Csv(v), N(totalUsers), N(views)));
        BreakdownRows++;
        return true;
    }

    /// <summary>Writes the zip. The writer can't be used afterwards.</summary>
    public async Task WriteZipAsync(Stream output, CancellationToken ct = default)
    {
        var suffix = $"_{_start:yyyyMMdd}_{_end:yyyyMMdd}.csv";
        foreach (var (_, table) in _tables) await table.Writer.DisposeAsync();

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var (name, table) in _tables)
        {
            var entry = zip.CreateEntry(name + suffix, CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await using var file = File.OpenRead(table.Path);
            await file.CopyToAsync(entryStream, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (_, table) in _tables)
        {
            await table.Writer.DisposeAsync();
            try { File.Delete(table.Path); } catch (IOException) { }
        }
        _tables.Clear();
    }

    private void Open(string table, string header)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tempoo-ga4-{Guid.NewGuid():N}.csv");
        var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
        writer.WriteLine(header);
        _tables[table] = (path, writer);
    }

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Csv(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
