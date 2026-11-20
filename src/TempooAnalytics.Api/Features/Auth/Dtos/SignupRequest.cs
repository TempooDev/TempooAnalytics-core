namespace TempooAnalytics.Api.Features.Auth.Dtos;

public record SignupRequest(string Username, string Password, string? Email, bool IsAdmin = false);
