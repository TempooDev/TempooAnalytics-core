using System.Text.Json.Serialization;

namespace TempooAnalytics.Api.Features.Analytics.Dtos;

public record UserSiteDto(
    [property: JsonPropertyName("siteId")] Guid SiteId,
    [property: JsonPropertyName("siteKey")] string SiteKey,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("writeKey")] string? WriteKey = null,
    [property: JsonPropertyName("allowedOrigins")] string[]? AllowedOrigins = null
);
