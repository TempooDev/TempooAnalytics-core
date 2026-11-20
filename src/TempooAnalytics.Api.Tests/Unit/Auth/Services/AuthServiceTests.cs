using System;
using System.Threading.Tasks;
using Moq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Auth;
using TempooAnalytics.Api.Features.Auth.Services;
using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Email;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Auth.Services;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IConfiguration> _configMock = new();
    private readonly Mock<ILogger<AuthService>> _loggerMock = new();
    private readonly Mock<IEmailService> _emailRepoMock = new();
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        var keys = new Mock<IJwtKeyProvider>();
        keys.Setup(k => k.GetKey()).Returns("test-key-long-enough-for-hmac-sha256-123456");
        _service = new AuthService(_userRepoMock.Object, _configMock.Object, _loggerMock.Object, _emailRepoMock.Object, keys.Object);
    }

    [Fact]
    public async Task LoginAsync_ReturnsSuccess_WhenCredentialsAreValid()
    {
        var login = new LoginRequest("user", "pass");
        var userId = UserId.New();
        _userRepoMock.Setup(r => r.ValidateCredentialsAsync("user", "pass")).ReturnsAsync(true);
        _userRepoMock.Setup(r => r.FindByUsernameAsync("user")).ReturnsAsync(new User { Id = userId, Username = "user" });
        var result = await _service.LoginAsync(login);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.AccessToken);
    }

    [Fact]
    public async Task LoginAsync_IssuesRefreshTokenValidFor30Days()
    {
        _userRepoMock.Setup(r => r.ValidateCredentialsAsync("user", "pass")).ReturnsAsync(true);
        _userRepoMock.Setup(r => r.FindByUsernameAsync("user")).ReturnsAsync(new User { Id = UserId.New(), Username = "user" });

        var result = await _service.LoginAsync(new LoginRequest("user", "pass"));

        var validTo = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(result.Value!.RefreshToken).ValidTo;
        Assert.Equal(TimeSpan.FromDays(30), AuthService.RefreshTokenLifetime);
        Assert.InRange(validTo, DateTime.UtcNow.AddDays(30).AddMinutes(-1), DateTime.UtcNow.AddDays(30).AddMinutes(1));
    }

    [Fact]
    public async Task RefreshAsync_AcceptsTheRefreshTokenIssuedAtLogin()
    {
        var userId = UserId.New();
        _userRepoMock.Setup(r => r.ValidateCredentialsAsync("user", "pass")).ReturnsAsync(true);
        _userRepoMock.Setup(r => r.FindByUsernameAsync("user")).ReturnsAsync(new User { Id = userId, Username = "user", IsAdmin = true });
        var login = await _service.LoginAsync(new LoginRequest("user", "pass"));

        var result = await _service.RefreshAsync(login.Value!.RefreshToken);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var access = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(result.Value!.AccessToken);
        Assert.Equal(userId.ToString(), access.Subject);
        Assert.Contains(access.Claims, c => c.Type == "is_admin" && c.Value == "true");
    }
}
