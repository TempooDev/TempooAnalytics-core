using System.Text.Json.Serialization;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Auth.Dtos;

public record UserSiteDto(SiteId Id, [property: JsonPropertyName("site_key")] string SiteKey, string Name);
