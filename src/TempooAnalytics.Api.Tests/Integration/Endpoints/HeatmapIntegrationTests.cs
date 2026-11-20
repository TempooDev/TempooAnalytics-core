using TempooAnalytics.Api.Features.Billing;
using TempooAnalytics.Api.Features.Plans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Heatmaps;
using TempooAnalytics.Api.Infrastructure.Common;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

/// <summary>
/// Heatmap endpoints with the real site access check (Postgres) and ClickHouse replaced by a mock.
/// </summary>
public class HeatmapIntegrationTests : FunctionalTestBase
{
    private readonly Mock<IHeatmapRepository> _heatmaps = new();
    private readonly Mock<IPlanPolicy> _plan = new();
    private readonly Guid _ownerId = Guid.NewGuid();

    public HeatmapIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        UserOnPlan(advanced: true);
        _heatmaps.Setup(h => h.GetPagesAsync(It.IsAny<SiteId>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new HeatmapPageDto("/precios", 12, 2, 1)]);
        _heatmaps.Setup(h => h.GetPointsAsync(It.IsAny<SiteId>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new HeatmapPointDto(510, 1340, 3)]);
        _heatmaps.Setup(h => h.GetTopElementsAsync(It.IsAny<SiteId>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new HeatmapElementDto("a#buy", 3, 1, 0)]);
        _heatmaps.Setup(h => h.GetTotalClicksAsync(It.IsAny<SiteId>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _heatmaps.Setup(h => h.GetScrollHistogramAsync(It.IsAny<SiteId>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([(100, 1L)]);
    }

    // The plan seam of the core (IPlanPolicy), so the test does not depend on how a build decides it (cloud: tier row; community: always yes).
    private void UserOnPlan(bool advanced) =>
        _plan.Setup(p => p.HasAdvancedAnalyticsAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>())).ReturnsAsync(advanced);

    private HttpClient CreateClient(Guid? userId, bool flagEnabled = true)
    {
        var client = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, conf) => conf.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:Heatmaps"] = flagEnabled ? "true" : "false"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => _heatmaps.Object);
                services.AddScoped(_ => _plan.Object);
            });
        }).CreateClient();
        if (userId.HasValue) AuthenticateClient(client, userId.Value, false);
        return client;
    }

    private async Task<SiteId> AddSiteAsync(string siteKey, params string[] allowedOrigins)
    {
        var siteId = SiteId.New();
        using var context = Fixture.CreateContext();
        context.AnalyticsSites.Add(new AnalyticsSite
        {
            Id = siteId,
            SiteKey = siteKey,
            Name = siteKey,
            OwnerId = new UserId(_ownerId),
            AllowedOrigins = allowedOrigins,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        return siteId;
    }

    [Fact]
    public async Task Heatmap_ForOwnerWithAdvancedAnalytics_ReturnsTheMap()
    {
        await AddSiteAsync("hm-own");
        var client = CreateClient(_ownerId);

        var pages = await client.GetAsync("/api/analytics/heatmaps/pages?siteId=hm-own");
        var map = await client.GetAsync("/api/analytics/heatmaps?siteId=hm-own&path=%2Fprecios&device=desktop");

        pages.StatusCode.Should().Be(HttpStatusCode.OK);
        map.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await map.Content.ReadFromJsonAsync<HeatmapDto>();
        body!.TotalClicks.Should().Be(3);
        body.Points.Should().ContainSingle();
        body.Scroll.Should().HaveCount(10);
    }

    [Fact]
    public async Task Heatmap_WithoutSession_Returns401()
    {
        await AddSiteAsync("hm-anon");
        var response = await CreateClient(null).GetAsync("/api/analytics/heatmaps?siteId=hm-anon&path=%2F");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Heatmap_ForSiteOfSomeoneElse_Returns403()
    {
        await AddSiteAsync("hm-victim");
        var client = CreateClient(Guid.NewGuid());

        var map = await client.GetAsync("/api/analytics/heatmaps?siteId=hm-victim&path=%2F");
        var pages = await client.GetAsync("/api/analytics/heatmaps/pages?siteId=hm-victim");
        var token = await client.PostAsJsonAsync("/api/analytics/heatmaps/overlay-token", new { siteId = "hm-victim", path = "/" });

        map.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        pages.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        token.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Heatmap_WithoutAdvancedAnalytics_Returns403()
    {
        await AddSiteAsync("hm-basic");
        UserOnPlan(advanced: false);

        var response = await CreateClient(_ownerId).GetAsync("/api/analytics/heatmaps/pages?siteId=hm-basic");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Overlay_WithValidToken_AllowsOnlyTheSiteOrigin()
    {
        await AddSiteAsync("hm-overlay", "tienda.es");
        var owner = CreateClient(_ownerId);
        var issued = await owner.PostAsJsonAsync("/api/analytics/heatmaps/overlay-token", new { siteId = "hm-overlay", path = "/precios" });
        issued.StatusCode.Should().Be(HttpStatusCode.OK);
        var grant = await issued.Content.ReadFromJsonAsync<HeatmapOverlayTokenResponse>();
        grant!.SiteUrl.Should().Be("https://tienda.es");

        var anonymous = CreateClient(null);
        var url = $"/api/public/heatmaps/overlay?token={Uri.EscapeDataString(grant.Token)}&siteId=hm-overlay&device=desktop";

        var fromSite = new HttpRequestMessage(HttpMethod.Get, url);
        fromSite.Headers.Add("Origin", "https://tienda.es");
        var allowed = await anonymous.SendAsync(fromSite);

        var fromElsewhere = new HttpRequestMessage(HttpMethod.Get, url);
        fromElsewhere.Headers.Add("Origin", "https://otra-web.com");
        var blocked = await anonymous.SendAsync(fromElsewhere);

        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        allowed.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("https://tienda.es");
        (await allowed.Content.ReadFromJsonAsync<HeatmapDto>())!.Path.Should().Be("/precios");
        blocked.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Overlay_TokenForAnotherSite_Returns401()
    {
        await AddSiteAsync("hm-token-site", "tienda.es");
        var issued = await CreateClient(_ownerId).PostAsJsonAsync("/api/analytics/heatmaps/overlay-token", new { siteId = "hm-token-site", path = "/" });
        var grant = await issued.Content.ReadFromJsonAsync<HeatmapOverlayTokenResponse>();

        var response = await CreateClient(null).GetAsync($"/api/public/heatmaps/overlay?token={Uri.EscapeDataString(grant!.Token)}&siteId=otro-sitio");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Heatmaps_WithFlagOff_Return404()
    {
        await AddSiteAsync("hm-flag-off");
        var client = CreateClient(_ownerId, flagEnabled: false);

        var pages = await client.GetAsync("/api/analytics/heatmaps/pages?siteId=hm-flag-off");
        var map = await client.GetAsync("/api/analytics/heatmaps?siteId=hm-flag-off&path=%2F");
        var token = await client.PostAsJsonAsync("/api/analytics/heatmaps/overlay-token", new { siteId = "hm-flag-off", path = "/" });
        var overlay = await CreateClient(null, flagEnabled: false).GetAsync("/api/public/heatmaps/overlay?token=x");
        var features = await client.GetFromJsonAsync<Dictionary<string, bool>>("/api/features");

        pages.StatusCode.Should().Be(HttpStatusCode.NotFound);
        map.StatusCode.Should().Be(HttpStatusCode.NotFound);
        token.StatusCode.Should().Be(HttpStatusCode.NotFound);
        overlay.StatusCode.Should().Be(HttpStatusCode.NotFound);
        features!["heatmaps"].Should().BeFalse();
    }

    [Fact]
    public async Task Overlay_WithInvalidToken_Returns401()
    {
        var response = await CreateClient(null).GetAsync("/api/public/heatmaps/overlay?token=manipulado");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
