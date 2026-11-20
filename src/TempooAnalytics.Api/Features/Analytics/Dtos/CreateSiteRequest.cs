namespace TempooAnalytics.Api.Features.Analytics.Dtos;

public record CreateSiteRequest(string Name, string? SiteKey, string[]? AllowedOrigins);
