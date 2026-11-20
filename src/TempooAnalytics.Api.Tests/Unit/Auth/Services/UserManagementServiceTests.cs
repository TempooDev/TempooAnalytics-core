using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Moq;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Auth.Services;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Infrastructure.Email;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Auth.Services;

public class UserManagementServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IEmailService> _emailRepoMock = new();
    private readonly Mock<HybridCache> _cacheMock = new();
    private readonly Mock<ILogger<UserManagementService>> _loggerMock = new();
    private readonly UserManagementService _service;

    public UserManagementServiceTests()
    {
        _service = new UserManagementService(_userRepoMock.Object, _cacheMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task SignupAsync_ReturnsSuccess_WhenUserDoesNotExist()
    {
        var request = new SignupRequest("newuser", "password", "test@test.com");
        _userRepoMock.Setup(r => r.FindByUsernameAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        var result = await _service.SignupAsync(request);
        Assert.True(result.IsSuccess);
        Assert.Equal("newuser", result.Value!.Username);
    }

    [Fact]
    public async Task GetUserProfileAsync_ReturnsNotFound_WhenUserDoesNotExist()
    {
        var userId = UserId.New();
        _userRepoMock.Setup(r => r.FindByIdAsync(userId)).ReturnsAsync((User?)null);
        var result = await _service.GetUserProfileAsync(userId);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
    }
}
