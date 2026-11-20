using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TempooAnalytics.Api.Features.Imports.Ga4;

public sealed record GoogleAccessToken(string Value, DateTimeOffset ExpiresAt, string? Scope);

public sealed record Ga4Property(string Id, string Name, string Account);

/// <summary>A failed call to a Google API. Its message never includes request data (codes or tokens).</summary>
public sealed class GoogleApiException(HttpStatusCode? statusCode, string message) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

/// <summary>
/// Calls to Google's OAuth token endpoint, the GA4 Admin API and the GA4 Data API. The access token travels
/// only in the Authorization header; nothing here logs request or response bodies.
/// </summary>
public interface IGoogleAnalyticsClient
{
    Task<GoogleAccessToken> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct = default);
    Task<IReadOnlyList<Ga4Property>> ListPropertiesAsync(string accessToken, CancellationToken ct = default);

    /// <summary>Creation time of the property (Admin API <c>properties.get</c>).</summary>
    Task<DateTimeOffset?> GetPropertyCreateTimeAsync(string accessToken, string propertyId, CancellationToken ct = default);

    /// <summary>One page of a Data API <c>runReport</c>.</summary>
    Task<RunReportResponse> RunReportAsync(string accessToken, string propertyId, RunReportRequest request, CancellationToken ct = default);
}

public sealed class GoogleAnalyticsClient(HttpClient http, IOptionsMonitor<GoogleOAuthOptions> options, TimeProvider timeProvider) : IGoogleAnalyticsClient
{
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string AdminApi = "https://analyticsadmin.googleapis.com/v1beta";
    private const string DataApi = "https://analyticsdata.googleapis.com/v1beta";

    public async Task<GoogleAccessToken> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct = default)
    {
        var o = options.CurrentValue;
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["client_id"] = o.ClientId ?? "",
                ["client_secret"] = o.ClientSecret ?? "",
                ["redirect_uri"] = o.RedirectUri ?? ""
            })
        };
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, ct);
            throw new GoogleApiException(response.StatusCode, error == "invalid_grant"
                ? "The Google authorization expired or was already used. Connect with Google again."
                : "Google did not accept the authorization. Connect with Google again.");
        }

        var token = await response.Content.ReadFromJsonAsync(Ga4JsonContext.Default.GoogleTokenResponse, ct);
        if (string.IsNullOrEmpty(token?.AccessToken))
            throw new GoogleApiException(response.StatusCode, "Google did not return an access token. Connect with Google again.");

        return new GoogleAccessToken(token.AccessToken, timeProvider.GetUtcNow().AddSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 3600), token.Scope);
    }

    public async Task<IReadOnlyList<Ga4Property>> ListPropertiesAsync(string accessToken, CancellationToken ct = default)
    {
        var properties = new List<Ga4Property>();
        string? pageToken = null;
        do
        {
            var url = $"{AdminApi}/accountSummaries?pageSize=200";
            if (pageToken != null) url += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            var page = await SendAsync(accessToken, new HttpRequestMessage(HttpMethod.Get, url), Ga4JsonContext.Default.AccountSummariesResponse, ct);
            foreach (var account in page.AccountSummaries ?? [])
            {
                foreach (var property in account.PropertySummaries ?? [])
                {
                    if (string.IsNullOrEmpty(property.Property)) continue;
                    properties.Add(new Ga4Property(property.Property, property.DisplayName ?? property.Property, account.DisplayName ?? account.Account ?? ""));
                }
            }
            pageToken = string.IsNullOrEmpty(page.NextPageToken) ? null : page.NextPageToken;
        } while (pageToken != null);

        return properties;
    }

    public async Task<DateTimeOffset?> GetPropertyCreateTimeAsync(string accessToken, string propertyId, CancellationToken ct = default)
    {
        var property = await SendAsync(accessToken, new HttpRequestMessage(HttpMethod.Get, $"{AdminApi}/{PropertyPath(propertyId)}"), Ga4JsonContext.Default.PropertyResponse, ct);
        return property.CreateTime;
    }

    public Task<RunReportResponse> RunReportAsync(string accessToken, string propertyId, RunReportRequest request, CancellationToken ct = default)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"{DataApi}/{PropertyPath(propertyId)}:runReport")
        {
            Content = JsonContent.Create(request, Ga4JsonContext.Default.RunReportRequest)
        };
        return SendAsync(accessToken, message, Ga4JsonContext.Default.RunReportResponse, ct);
    }

    // "properties/123" only: the id comes from the browser and ends up in a URL path.
    private static string PropertyPath(string propertyId)
    {
        if (!Ga4PropertyId.IsValid(propertyId)) throw new ArgumentException("Invalid GA4 property id.", nameof(propertyId));
        return propertyId;
    }

    private async Task<T> SendAsync<T>(string accessToken, HttpRequestMessage request, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        using (request)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new GoogleApiException(response.StatusCode, response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                        "Google Analytics denied access (it may have been revoked). Connect with Google again.",
                    HttpStatusCode.TooManyRequests =>
                        "The Google Analytics quota for this property is exhausted. Try again tomorrow.",
                    _ => "Google Analytics is not available right now. Try again later."
                });
            }
            return await response.Content.ReadFromJsonAsync(typeInfo, ct)
                ?? throw new GoogleApiException(response.StatusCode, "Google Analytics returned an empty response. Try again later.");
        }
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return (await response.Content.ReadFromJsonAsync(Ga4JsonContext.Default.GoogleErrorResponse, ct))?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public static class Ga4PropertyId
{
    public static bool IsValid(string? propertyId) =>
        propertyId is { Length: > 11 and <= 40 } &&
        propertyId.StartsWith("properties/", StringComparison.Ordinal) &&
        propertyId.AsSpan("properties/".Length).IndexOfAnyExceptInRange('0', '9') < 0;
}

// --- Google API contracts (only the fields used) ---

public sealed record GoogleTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("scope")] string? Scope);

public sealed record GoogleErrorResponse([property: JsonPropertyName("error")] string? Error);

public sealed record AccountSummariesResponse(List<AccountSummary>? AccountSummaries, string? NextPageToken);
public sealed record AccountSummary(string? Account, string? DisplayName, List<PropertySummary>? PropertySummaries);
public sealed record PropertySummary(string? Property, string? DisplayName);

public sealed record PropertyResponse(string? Name, DateTimeOffset? CreateTime);

public sealed record RunReportRequest(
    List<DateRange> DateRanges,
    List<NamedItem> Dimensions,
    List<NamedItem> Metrics,
    long Limit,
    long Offset,
    bool KeepEmptyRows,
    List<OrderBy>? OrderBys = null);

public sealed record DateRange(string StartDate, string EndDate);
public sealed record NamedItem(string Name);
public sealed record OrderBy(MetricOrderBy Metric, bool Desc);
public sealed record MetricOrderBy(string MetricName);

public sealed record RunReportResponse(List<ReportRow>? Rows, long RowCount);
public sealed record ReportRow(List<ReportValue>? DimensionValues, List<ReportValue>? MetricValues);
public sealed record ReportValue(string? Value);
