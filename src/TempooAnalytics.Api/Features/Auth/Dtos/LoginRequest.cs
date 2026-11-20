namespace TempooAnalytics.Api.Features.Auth.Dtos;

public record LoginRequest(string Username, string Password, bool RememberMe = false);
