using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TempooAnalytics.Api.Tests.Unit.Imports;

/// <summary>TimeProvider whose clock only moves when the test says so.</summary>
public sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// Stands in for Google's OAuth, Admin and Data APIs. Reports are answered by <see cref="Reports"/>,
/// which by default returns one row per day in <see cref="DataDays"/> and two values per breakdown.
/// </summary>
public sealed class FakeGoogleHandler : HttpMessageHandler
{
    public const string AccessToken = "ya29.fake-google-access-token";
    public const string PropertyId = "properties/123";

    public ConcurrentQueue<(HttpMethod Method, string Url, string Body)> Requests { get; } = new();
    public DateTimeOffset PropertyCreated { get; set; } = new(2024, 1, 15, 9, 30, 0, TimeSpan.Zero);
    public HashSet<DateOnly> DataDays { get; set; } = [new(2024, 1, 20), new(2024, 2, 3)];

    /// <summary>Status returned by the Data API (e.g. 401 or 429) instead of reports.</summary>
    public HttpStatusCode? DataApiStatus { get; set; }

    public string GrantedScope { get; set; } = "https://www.googleapis.com/auth/analytics.readonly";
    public bool NoProperties { get; set; }

    public Func<JsonElement, string>? Reports { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var url = request.RequestUri!.AbsoluteUri;
        Requests.Enqueue((request.Method, url, body));

        if (url.StartsWith("https://oauth2.googleapis.com/token"))
        {
            return Json($$"""{"access_token":"{{AccessToken}}","expires_in":3599,"scope":"{{GrantedScope}}","token_type":"Bearer"}""");
        }

        if (request.Headers.Authorization?.Parameter != AccessToken)
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        if (url.Contains("/accountSummaries"))
        {
            return Json(NoProperties
                ? "{}"
                : """{"accountSummaries":[{"account":"accounts/9","displayName":"Acme","propertySummaries":[{"property":"properties/123","displayName":"acme.com"},{"property":"properties/456","displayName":"blog.acme.com"}]}]}""");
        }

        if (url.EndsWith("/v1beta/properties/123") || url.EndsWith("/v1beta/properties/456"))
            return Json($$"""{"name":"properties/123","createTime":"{{PropertyCreated:yyyy-MM-ddTHH:mm:ss}}.000Z"}""");

        if (url.Contains(":runReport"))
        {
            if (DataApiStatus is { } status) return new HttpResponseMessage(status);
            using var doc = JsonDocument.Parse(body);
            return Json((Reports ?? DefaultReports)(doc.RootElement));
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private string DefaultReports(JsonElement request)
    {
        var range = request.GetProperty("dateRanges")[0];
        var from = DateOnly.Parse(range.GetProperty("startDate").GetString()!, CultureInfo.InvariantCulture);
        var to = DateOnly.Parse(range.GetProperty("endDate").GetString()!, CultureInfo.InvariantCulture);
        var dimensions = request.GetProperty("dimensions").EnumerateArray().Select(d => d.GetProperty("name").GetString()!).ToList();
        var days = DataDays.Where(d => d >= from && d <= to).Order().ToList();

        var rows = new List<string>();
        foreach (var day in days)
        {
            var date = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            if (dimensions.Count == 1)
            {
                rows.Add(Row([date], ["120", "400", "150", "90", "60"]));
                continue;
            }
            string[] values = dimensions[1] switch
            {
                "sessionSource" => ["google", "(direct)"],
                "countryId" => ["ES", "(not set)"],
                "deviceCategory" => ["desktop", "mobile"],
                "pagePath" => ["/", "/blog, \"news\""],
                _ => ["A", "(other)"]
            };
            rows.AddRange(values.Select(v => Row([date, v], ["10", "20"])));
        }
        return $$"""{"rows":[{{string.Join(',', rows)}}],"rowCount":{{rows.Count}}}""";
    }

    public static string Row(string[] dimensions, string[] metrics) =>
        $$"""{"dimensionValues":[{{string.Join(',', dimensions.Select(v => $$"""{"value":{{JsonSerializer.Serialize(v)}}}"""))}}],"metricValues":[{{string.Join(',', metrics.Select(v => $$"""{"value":"{{v}}"}"""))}}]}""";

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>Collects every log message (formatted, with exceptions) at every level.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);
    public void Dispose() { }

    private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            provider.Messages.Enqueue($"{category} scope: {state}");
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var structured = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(' ', pairs.Select(p => $"{p.Key}={p.Value}"))
                : "";
            provider.Messages.Enqueue($"{category} [{logLevel}] {formatter(state, exception)} {structured} {exception}");
        }
    }
}
