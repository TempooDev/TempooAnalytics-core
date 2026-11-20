namespace TempooAnalytics.Api.Features.Auth.Dtos;

public record TokenRequest(string UserId, string? DisplayName, bool IsAdmin = false);
