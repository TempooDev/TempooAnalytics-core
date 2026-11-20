using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using Moq;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Features.Heatmaps;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Infrastructure.Common;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Heatmaps;

public class HeatmapServiceTests
{
    private readonly Mock<IHeatmapRepository> _heatmaps = new();
    private readonly Mock<ISiteRepository> _sites = new();
    private readonly Mock<ISiteAccessService> _access = new();
    private readonly Mock<IPlanPolicy> _plan = new();
    private readonly HeatmapService _service;

    private readonly UserId _user = new(Guid.NewGuid());
    private readonly UserId _owner = new(Guid.NewGuid());
    private readonly AnalyticsSite _site;

    public HeatmapServiceTests()
    {
        _site = new() { Id = SiteId.New(), SiteKey = "tienda", Name = "tienda.es", AllowedOrigins = [], OwnerId = _owner };
        _sites.Setup(s => s.GetSiteByIdAsync(_site.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_site);
        _sites.Setup(s => s.GetSiteByKeyAsync("tienda", It.IsAny<CancellationToken>())).ReturnsAsync(_site);
        _access.Setup(a => a.CanAccessAsync(_user, _site.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        OnPlan(advanced: true);
        _heatmaps.Setup(h => h.GetScrollHistogramAsync(It.IsAny<SiteId>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(int, long)> { (100, 1), (40, 1) });
        _heatmaps.Setup(h => h.GetTotalClicksAsync(It.IsAny<SiteId>(), "/precios", It.IsAny<DateTime>(), It.IsAny<DateTime>(), "desktop", It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        var tokens = new HeatmapOverlayTokenService(new EphemeralDataProtectionProvider(), TimeProvider.System);
        _service = new HeatmapService(_heatmaps.Object, _sites.Object, _access.Object, _plan.Object, tokens);
    }

    // The plan that counts is the site owner's; the member asking (_user) has none.
    private void OnPlan(bool advanced) =>
        _plan.Setup(p => p.HasAdvancedAnalyticsAsync(_owner, It.IsAny<CancellationToken>())).ReturnsAsync(advanced);

    [Fact]
    public async Task GetHeatmap_WithAccessAndPlan_ReturnsTheMap()
    {
        var result = await _service.GetHeatmapAsync(_user, "tienda", "/precios", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, "desktop", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalClicks.Should().Be(7);
        result.Value.Path.Should().Be("/precios");
        result.Value.Scroll.Should().ContainSingle(s => s.Depth == 50 && s.ReachedPct == 50);
    }

    [Fact]
    public async Task GetHeatmap_SiteOfSomeoneElse_IsForbidden()
    {
        _access.Setup(a => a.CanAccessAsync(_user, _site.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _service.GetHeatmapAsync(_user, _site.Id.Value.ToString(), "/", DateTime.UtcNow, DateTime.UtcNow, "desktop", CancellationToken.None);

        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
        _heatmaps.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetPages_WithoutAdvancedAnalytics_IsForbidden()
    {
        OnPlan(advanced: false);

        var result = await _service.GetPagesAsync(_user, "tienda", DateTime.UtcNow, DateTime.UtcNow, "desktop", CancellationToken.None);

        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task IssueOverlayToken_ReturnsTokenAndSiteUrl()
    {
        var result = await _service.IssueOverlayTokenAsync(_user, new HeatmapOverlayTokenRequest("tienda", "/precios"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Token.Should().NotBeNullOrEmpty();
        result.Value.SiteUrl.Should().Be("https://tienda.es");
    }

    [Fact]
    public async Task IssueOverlayToken_RejectsARelativePath()
    {
        var result = await _service.IssueOverlayTokenAsync(_user, new HeatmapOverlayTokenRequest("tienda", "https://evil.com"), CancellationToken.None);

        result.ErrorType.Should().Be(ResultErrorType.Validation);
    }
}
