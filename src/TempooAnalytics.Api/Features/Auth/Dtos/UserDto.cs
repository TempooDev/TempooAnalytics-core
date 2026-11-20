namespace TempooAnalytics.Api.Features.Auth.Dtos;

public record UserDto(Guid Id, string Username, string? Email, bool IsAdmin, bool IsActive, DateTime CreatedAt);
