using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Microsoft.Extensions.Configuration;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Models;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Services;

public class SiteServiceTests
{
    private readonly Mock<ISiteRepository> _repoMock = new();
    // The plan seam of the core: whether the owner may create one more site (cloud: tier limit; community: always).
    private readonly Mock<IPlanPolicy> _planMock = new();
    private readonly Mock<IConfiguration> _configMock = new();
    private readonly SiteService _service;

    public SiteServiceTests()
    {
        _service = new SiteService(_repoMock.Object, _planMock.Object);
        _planMock.Setup(p => p.CanCreateSiteAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    [Fact]
    public async Task CreateSiteAsync_ReturnsConflict_WhenSiteExists()
    {
        var ownerId = UserId.New();
        var request = new CreateSiteRequest("Site", "existing", null);
        
        // No owned sites yet, so the limit passes
        _repoMock.Setup(r => r.CountOwnedSitesAsync(ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        
        _repoMock.Setup(r => r.ExistsAsync("existing", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        
        var result = await _service.CreateSiteAsync(ownerId, request, CancellationToken.None);
        
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task CreateSiteAsync_Fails_WhenOwnedSitesReachPlanLimit()
    {
        var ownerId = UserId.New();
        _planMock.Setup(p => p.CanCreateSiteAsync(ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repoMock.Setup(r => r.CountOwnedSitesAsync(ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await _service.CreateSiteAsync(ownerId, new CreateSiteRequest("Second", null, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.Failure, result.ErrorType);
        _repoMock.Verify(r => r.CreateSiteAsync(It.IsAny<AnalyticsSite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateWriteKeyAsync_ReturnsUnauthorized_WhenNotOwner()
    {
        var userId = UserId.New();
        var otherId = UserId.New();
        _repoMock.Setup(r => r.GenerateWriteKeyAsync("site", userId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(("site", "key", otherId));
        var result = await _service.GenerateWriteKeyAsync(userId, "site", CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.Unauthorized, result.ErrorType);
    }
}
