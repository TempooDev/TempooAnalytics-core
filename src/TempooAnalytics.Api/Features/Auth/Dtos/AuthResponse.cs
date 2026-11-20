using System.Text.Json.Serialization;

namespace TempooAnalytics.Api.Features.Auth.Dtos;

public record AuthResponse(
    [property: JsonPropertyName("accessToken")] string AccessToken, 
    [property: JsonPropertyName("refreshToken")] string RefreshToken, 
    [property: JsonPropertyName("expiresIn")] int ExpiresIn);
