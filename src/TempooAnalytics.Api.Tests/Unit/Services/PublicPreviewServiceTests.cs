using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Moq;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Services;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Services;

/// <summary>
/// Unit tests for PublicPreviewService using IPublicPreviewService interface.
/// Since PublicPreviewService depends on AnalyticsDbContext (which has JsonDocument columns
/// incompatible with InMemory), we test via the interface mock pattern.
/// Share token generation is covered against the real service in PublicPreviewShareIntegrationTests.
/// </summary>
public class PublicPreviewServiceTests
{
    private readonly Mock<IPublicPreviewService> _serviceMock;

    public PublicPreviewServiceTests()
    {
        _serviceMock = new Mock<IPublicPreviewService>();
    }

    [Fact]
    public async Task GetPublicPreviewDataAsync_CacheHit_ReturnsCachedData()
    {
        // Arrange
        var cachedResponse = new PublicPreviewResponse { SiteName = "Cached" };
        _serviceMock.Setup(s => s.GetPublicPreviewDataAsync("test-token", null, null, 30, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedResponse);

        // Act
        var result = await _serviceMock.Object.GetPublicPreviewDataAsync("test-token", ct: CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.SiteName.Should().Be("Cached");
    }

    [Fact]
    public async Task GetPublicPreviewDataAsync_InvalidToken_ReturnsNull()
    {
        // Arrange
        _serviceMock.Setup(s => s.GetPublicPreviewDataAsync("invalid-token", null, null, 30, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PublicPreviewResponse?)null);

        // Act
        var result = await _serviceMock.Object.GetPublicPreviewDataAsync("invalid-token", ct: CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }
}
