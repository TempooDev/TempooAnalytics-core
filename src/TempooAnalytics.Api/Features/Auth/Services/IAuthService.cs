using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Auth.Services;

public interface IAuthService : IService
{
    Task<Result<AuthResponse>> LoginAsync(LoginRequest login);
    Task<Result<AuthResponse>> RefreshAsync(string refreshToken);
    Task<Result> ForgotPasswordAsync(string email);
    Task<AuthResponse> CreateDevTokenAsync(TokenRequest req);
    Task<Result<AuthResponse>> CreateAdminSessionAsync(AdminSessionRequest req);
}
