namespace TempooAnalytics.Api.Features.Auth.Dtos;

public record ChangePasswordRequest(string OldPassword, string NewPassword);
