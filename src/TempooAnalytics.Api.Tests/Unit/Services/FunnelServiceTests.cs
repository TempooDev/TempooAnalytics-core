using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Moq;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Infrastructure.Common;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Services;

public class FunnelServiceTests
{
    private readonly Mock<IFunnelRepository> _funnels = new();
    private readonly Mock<ISiteRepository> _sites = new();
    private readonly Mock<ISiteAccessService> _access = new();
    private readonly FunnelService _service;

    private readonly UserId _user = new(Guid.NewGuid());
    private readonly SiteId _siteId = SiteId.New();
    private readonly FunnelDefinition _funnel;

    public FunnelServiceTests()
    {
        _funnel = new FunnelDefinition { Id = Guid.NewGuid(), SiteId = _siteId, Name = "Checkout" };
        _funnels.Setup(f => f.GetFunnelDefinitionAsync(_funnel.Id, _siteId, It.IsAny<CancellationToken>())).ReturnsAsync(_funnel);
        _access.Setup(a => a.CanAccessAsync(_user, _siteId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _service = new FunnelService(_funnels.Object, _sites.Object, _access.Object);
    }

    [Fact]
    public async Task DeleteFunnel_WithAccess_RemovesIt()
    {
        var result = await _service.DeleteFunnelAsync(_user, _funnel.Id, _siteId.ToString(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _funnels.Verify(f => f.DeleteFunnelDefinitionAsync(_funnel, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteFunnel_Unknown_ReturnsNotFound()
    {
        var result = await _service.DeleteFunnelAsync(_user, Guid.NewGuid(), _siteId.ToString(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
        _funnels.Verify(f => f.DeleteFunnelDefinitionAsync(It.IsAny<FunnelDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteFunnel_WithoutAccess_ReturnsForbidden()
    {
        var stranger = new UserId(Guid.NewGuid());

        var result = await _service.DeleteFunnelAsync(stranger, _funnel.Id, _siteId.ToString(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
        _funnels.Verify(f => f.DeleteFunnelDefinitionAsync(It.IsAny<FunnelDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
