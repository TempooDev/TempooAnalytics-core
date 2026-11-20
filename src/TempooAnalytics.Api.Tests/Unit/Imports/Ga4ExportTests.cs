using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using TempooAnalytics.Api.Features.Imports.Ga4;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Imports;

public class Ga4ExportTests
{
    private static readonly DateOnly Start = new(2024, 1, 15);
    private static readonly DateOnly End = new(2024, 2, 10);

    private static async Task<Dictionary<string, string>> ReadZip(Ga4ExportWriter writer)
    {
        using var buffer = new MemoryStream();
        await writer.WriteZipAsync(buffer);
        buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(e => e.FullName, e => new StreamReader(e.Open()).ReadToEnd());
    }

    private static (FakeGoogleHandler Handler, Ga4ReportDownloader Downloader) Downloader()
    {
        var handler = new FakeGoogleHandler();
        var client = new GoogleAnalyticsClient(new HttpClient(handler), new StaticOptions(), TimeProvider.System);
        return (handler, new Ga4ReportDownloader(client));
    }

    [Fact]
    public async Task Totals_OfOneDay_MapToPlausibleMetrics()
    {
        // Spec scenario "Totales de un día".
        await using var writer = new Ga4ExportWriter(Start, End);
        writer.AddTotals(new DateOnly(2024, 1, 20), totalUsers: 120, views: 400, sessions: 150, engagedSessions: 90, averageSessionDuration: 60);

        var files = await ReadZip(writer);

        files["imported_visitors_20240115_20240210.csv"].Should().Be(
            "date,visitors,pageviews,bounces,visits,visit_duration\n2024-01-20,120,400,60,150,9000\n");
    }

    [Fact]
    public async Task Breakdowns_SkipGa4Placeholders_AndEscapeCsv()
    {
        await using var writer = new Ga4ExportWriter(Start, End);
        var day = new DateOnly(2024, 1, 20);

        writer.AddBreakdown(Ga4Breakdown.Sources, day, "(direct)", 5, 5).Should().BeFalse();
        writer.AddBreakdown(Ga4Breakdown.Sources, day, "(not set)", 5, 5).Should().BeFalse();
        writer.AddBreakdown(Ga4Breakdown.Browsers, day, "(other)", 5, 5).Should().BeFalse();
        writer.AddBreakdown(Ga4Breakdown.Sources, day, "google", 10, 20).Should().BeTrue();
        writer.AddBreakdown(Ga4Breakdown.Pages, day, "/a,\"b\"", 1, 2).Should().BeTrue();

        var files = await ReadZip(writer);

        files.Keys.Should().BeEquivalentTo(
            "imported_visitors_20240115_20240210.csv",
            "imported_pages_20240115_20240210.csv",
            "imported_sources_20240115_20240210.csv",
            "imported_locations_20240115_20240210.csv",
            "imported_devices_20240115_20240210.csv",
            "imported_browsers_20240115_20240210.csv",
            "imported_operating_systems_20240115_20240210.csv");
        files["imported_sources_20240115_20240210.csv"].Should().Be("date,source,visitors,pageviews\n2024-01-20,google,10,20\n");
        files["imported_pages_20240115_20240210.csv"].Should().Be("date,page,visitors,pageviews\n2024-01-20,\"/a,\"\"b\"\"\",1,2\n");
        files["imported_browsers_20240115_20240210.csv"].Should().Be("date,browser,visitors,pageviews\n");
        writer.BreakdownRows.Should().Be(2);
    }

    [Fact]
    public void Months_SplitTheRangeByCalendarMonth()
    {
        Ga4ReportDownloader.Months(new DateOnly(2023, 12, 20), new DateOnly(2024, 2, 5)).Should().Equal(
            (new DateOnly(2023, 12, 20), new DateOnly(2023, 12, 31)),
            (new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31)),
            (new DateOnly(2024, 2, 1), new DateOnly(2024, 2, 5)));
    }

    [Fact]
    public async Task Download_RequestsSevenReportsPerMonth_WithTheDecidedDimensionsAndMetrics()
    {
        var (handler, downloader) = Downloader();
        await using var writer = new Ga4ExportWriter(Start, End);

        await downloader.DownloadAsync(FakeGoogleHandler.AccessToken, FakeGoogleHandler.PropertyId, Start, End, writer);

        var reports = handler.Requests.Select(r => JsonDocument.Parse(r.Body).RootElement).ToList();
        reports.Should().HaveCount(14); // 2 months x 7 reports
        handler.Requests.Should().OnlyContain(r => r.Url == "https://analyticsdata.googleapis.com/v1beta/properties/123:runReport");

        string Dims(JsonElement r) => string.Join(',', r.GetProperty("dimensions").EnumerateArray().Select(d => d.GetProperty("name").GetString()));
        string Metrics(JsonElement r) => string.Join(',', r.GetProperty("metrics").EnumerateArray().Select(d => d.GetProperty("name").GetString()));
        string Range(JsonElement r) => $"{r.GetProperty("dateRanges")[0].GetProperty("startDate")}..{r.GetProperty("dateRanges")[0].GetProperty("endDate")}";

        reports.Take(7).Select(Dims).Should().Equal(
            "date", "date,pagePath", "date,sessionSource", "date,countryId", "date,deviceCategory", "date,browser", "date,operatingSystem");
        Metrics(reports[0]).Should().Be("totalUsers,screenPageViews,sessions,engagedSessions,averageSessionDuration");
        reports.Skip(1).Take(6).Select(Metrics).Should().AllBe("totalUsers,screenPageViews");
        reports.Take(7).Select(Range).Should().AllBe("2024-01-15..2024-01-31");
        reports.Skip(7).Select(Range).Should().AllBe("2024-02-01..2024-02-10");
        reports.Should().OnlyContain(r => !r.GetProperty("keepEmptyRows").GetBoolean());

        // Pages: the 10 000 most viewed per month, ordered by views; the rest page through 100 000 rows.
        var pages = reports[1];
        pages.GetProperty("limit").GetInt64().Should().Be(10_000);
        pages.GetProperty("orderBys")[0].GetProperty("metric").GetProperty("metricName").GetString().Should().Be("screenPageViews");
        pages.GetProperty("orderBys")[0].GetProperty("desc").GetBoolean().Should().BeTrue();
        reports[0].GetProperty("limit").GetInt64().Should().Be(100_000);

        writer.DailyRows.Should().Be(2);
        var files = await ReadZip(writer);
        files["imported_visitors_20240115_20240210.csv"].Should().Be(
            "date,visitors,pageviews,bounces,visits,visit_duration\n2024-01-20,120,400,60,150,9000\n2024-02-03,120,400,60,150,9000\n");
        files["imported_sources_20240115_20240210.csv"].Should().NotContain("direct");
    }

    [Fact]
    public async Task Download_FollowsPagination_UntilRowCount()
    {
        var (handler, downloader) = Downloader();
        var day = new DateOnly(2024, 1, 20);
        handler.Reports = request =>
        {
            var dims = request.GetProperty("dimensions").GetArrayLength();
            var offset = request.GetProperty("offset").GetInt64();
            if (dims > 1) return """{"rowCount":0}""";
            // 3 rows in total, served in two pages.
            var rows = offset switch
            {
                0 => new[] { FakeGoogleHandler.Row(["20240120"], ["1", "1", "1", "1", "1"]), FakeGoogleHandler.Row(["20240121"], ["1", "1", "1", "1", "1"]) },
                2 => new[] { FakeGoogleHandler.Row(["20240122"], ["1", "1", "1", "1", "1"]) },
                _ => Array.Empty<string>()
            };
            return $$"""{"rows":[{{string.Join(',', rows)}}],"rowCount":3}""";
        };
        await using var writer = new Ga4ExportWriter(day, new DateOnly(2024, 1, 31));

        await downloader.DownloadAsync(FakeGoogleHandler.AccessToken, FakeGoogleHandler.PropertyId, day, new DateOnly(2024, 1, 31), writer);

        writer.DailyRows.Should().Be(3);
        var totalsOffsets = handler.Requests
            .Select(r => JsonDocument.Parse(r.Body).RootElement)
            .Where(r => r.GetProperty("dimensions").GetArrayLength() == 1)
            .Select(r => r.GetProperty("offset").GetInt64());
        totalsOffsets.Should().Equal(0, 2);
    }

    [Fact]
    public async Task Download_WhenGoogleRejectsTheToken_ThrowsWithAReadableMessage()
    {
        var (handler, downloader) = Downloader();
        handler.DataApiStatus = System.Net.HttpStatusCode.TooManyRequests;
        await using var writer = new Ga4ExportWriter(Start, End);

        var act = () => downloader.DownloadAsync(FakeGoogleHandler.AccessToken, FakeGoogleHandler.PropertyId, Start, End, writer);

        (await act.Should().ThrowAsync<GoogleApiException>()).Which.Message.Should().Contain("quota");
    }

    [Fact]
    public async Task Client_ListsPropertiesAndCreateTime()
    {
        var handler = new FakeGoogleHandler();
        var client = new GoogleAnalyticsClient(new HttpClient(handler), new StaticOptions(), TimeProvider.System);

        var properties = await client.ListPropertiesAsync(FakeGoogleHandler.AccessToken);
        var created = await client.GetPropertyCreateTimeAsync(FakeGoogleHandler.AccessToken, "properties/123");

        properties.Should().Equal(new Ga4Property("properties/123", "acme.com", "Acme"), new Ga4Property("properties/456", "blog.acme.com", "Acme"));
        created.Should().Be(handler.PropertyCreated);
    }

    /// <summary>
    /// The Go worker parses this same file (internal/service/testdata/ga4_export.zip). Set
    /// WRITE_GA4_FIXTURE=1 to regenerate it after changing the format.
    /// </summary>
    [Fact]
    public async Task WrittenExport_MatchesTheGoWorkerFixture()
    {
        var (_, downloader) = Downloader();
        await using var writer = new Ga4ExportWriter(Start, End);
        await downloader.DownloadAsync(FakeGoogleHandler.AccessToken, FakeGoogleHandler.PropertyId, Start, End, writer);
        var files = await ReadZip(writer);

        var fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../TempooAnalytics.Go/internal/service/testdata/ga4_export.zip"));
        if (Environment.GetEnvironmentVariable("WRITE_GA4_FIXTURE") == "1")
        {
            await using var regenerated = new Ga4ExportWriter(Start, End);
            await downloader.DownloadAsync(FakeGoogleHandler.AccessToken, FakeGoogleHandler.PropertyId, Start, End, regenerated);
            await using var output = File.Create(fixture);
            await regenerated.WriteZipAsync(output);
        }

        using var zip = ZipFile.OpenRead(fixture);
        zip.Entries.ToDictionary(e => e.FullName, e => new StreamReader(e.Open()).ReadToEnd()).Should().BeEquivalentTo(files);
    }

    private sealed class StaticOptions : IOptionsMonitor<GoogleOAuthOptions>
    {
        public GoogleOAuthOptions CurrentValue { get; } = new() { ClientId = "client", ClientSecret = "secret", RedirectUri = "http://localhost:4200/account/imports/google/callback" };
        public GoogleOAuthOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<GoogleOAuthOptions, string?> listener) => null;
    }
}
