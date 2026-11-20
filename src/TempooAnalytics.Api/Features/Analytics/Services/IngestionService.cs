using System.Text.Json;
using StackExchange.Redis;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Services;

public class IngestionService(IAnalyticsEventRepository repo, IGeolocationService geoService, IServiceProvider sp) : IIngestionService
{
    public async Task<Result> ProcessEventAsync(AnalyticsEventDto dto, HttpRequest request, CancellationToken ct)
    {
        // Extract possible write key from Authorization header (Bearer) or X-API-Key
        string? writeKey = null;
        if (request.Headers.TryGetValue("Authorization", out var authHeader) && !string.IsNullOrWhiteSpace(authHeader))
        {
            var val = authHeader.ToString();
            if (val.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                writeKey = val.Substring("Bearer ".Length).Trim();
        }
        if (string.IsNullOrWhiteSpace(writeKey) && request.Headers.TryGetValue("X-API-Key", out var apiKeyHeader))
        {
            writeKey = apiKeyHeader.FirstOrDefault();
        }

        // Fallback: allow `?key=` query param (used by sendBeacon fallback)
        if (string.IsNullOrWhiteSpace(writeKey) && request.Query.TryGetValue("key", out var keyQuery) && !string.IsNullOrWhiteSpace(keyQuery))
        {
            writeKey = keyQuery.FirstOrDefault();
        }

        // Determine origin for origin-based validation (Origin header preferred, fall back to Referer)
        string? origin = null;
        if (request.Headers.TryGetValue("Origin", out var originHeader) && !string.IsNullOrWhiteSpace(originHeader))
        {
            origin = originHeader.FirstOrDefault();
        }
        else if (request.Headers.TryGetValue("Referer", out var refererHeader) && !string.IsNullOrWhiteSpace(refererHeader))
        {
            origin = refererHeader.FirstOrDefault();
        }

        // siteKey is sent in dto.SiteId
        var siteKey = dto.SiteId;

        var authorized = await repo.IsIngestAuthorizedAsync(siteKey, writeKey, origin, ct);

        if (!authorized)
        {
            return Result.Failure("Not authorized for ingestion", ResultErrorType.Unauthorized);
        }

        // --- Server-Side VisitorId Generation ---
        if (string.IsNullOrWhiteSpace(dto.VisitorId))
        {
            dto.VisitorId = geoService.GetVisitorId(request);
        }

        // --- Enrichment (Country, User-Agent, etc.) ---
        string? country = geoService.GetCountryCode(request);
        string? userAgent = request.Headers["User-Agent"].FirstOrDefault();

        try
        {
            var metadata = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(country)) metadata["country"] = country;
            if (!string.IsNullOrWhiteSpace(userAgent)) metadata["userAgent"] = userAgent;

            if (metadata.Count > 0)
            {
                if (dto.Data == null)
                {
                    dto.Data = metadata;
                }
                else if (dto.Data is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.Object)
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(je.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new Dictionary<string, object?>();
                        foreach (var kv in metadata)
                        {
                            if (!dict.ContainsKey(kv.Key)) dict[kv.Key] = kv.Value;
                        }
                        dto.Data = dict;
                    }
                    else
                    {
                        dto.Data = new { metadata, payload = dto.Data };
                    }
                }
                else if (dto.Data is IDictionary<string, object?> d)
                {
                    foreach (var kv in metadata)
                    {
                        if (!d.ContainsKey(kv.Key)) d[kv.Key] = kv.Value;
                    }
                    dto.Data = d;
                }
                else
                {
                    dto.Data = new { metadata, payload = dto.Data };
                }
            }
        }
        catch
        {
            // Ignore merge errors
        }

        // Lightweight producer: serialize and push to stream immediately.
        var redis = sp.GetService<IConnectionMultiplexer>();
        if (redis != null)
        {
            var db = redis.GetDatabase();
            var json = JsonSerializer.Serialize(dto, AppJsonContext.Default.AnalyticsEventDto);
            await db.StreamAddAsync("stream:analytics:events", "data", json);
        }

        return Result.Success();
    }
}
