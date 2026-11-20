using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Features.Billing;
using TempooAnalytics.Api.Features.Heatmaps;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.FeatureFlags;
using TempooAnalytics.Api.Infrastructure.Modules;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Modules;

/// <summary>The core on its own, as a self-hosted build runs it: no modules, no plan limits, no billing.</summary>
public partial class CoreWithoutModulesTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void ModulesDisabled_NothingIsDiscoveredOrRegistered()
    {
        var config = Config((FeatureModuleLoader.EnabledSetting, "false"));
        var services = new ServiceCollection();

        FeatureModuleLoader.Discover(config).Should().BeEmpty();
        services.AddFeatureModules(config, isTest: false);

        services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).Should().BeEmpty();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<FeatureModuleCatalog>().Modules.Should().BeEmpty();
        provider.GetServices<IFeatureFlagContributor>().Should().BeEmpty();
    }

    [Fact]
    public void FeaturesEndpoint_WithoutModules_OnlyShowsTheCoreFlags()
    {
        var config = Config(("FeatureFlags:Heatmaps", "true"), ("FeatureFlags:AiCredits", "true"), (FeatureModuleLoader.EnabledSetting, "false"));
        var services = new ServiceCollection();
        services.AddFeatureModules(config);
        using var provider = services.BuildServiceProvider();

        var response = FeatureFlagEndpoints.BuildResponse(new ConfigurationFeatureFlags(config), provider.GetServices<IFeatureFlagContributor>());

        response.Keys.Should().BeEquivalentTo(["plausibleImport", "ga4Import", "notifications", "heatmaps"]);
        response["heatmaps"].Should().BeTrue();
    }

    [Fact]
    public async Task CommunityPolicy_LetsASiteBeCreatedWhateverTheOwnedCount()
    {
        var sites = new Mock<ISiteRepository>();
        var owner = UserId.New();
        sites.Setup(r => r.CountOwnedSitesAsync(owner, It.IsAny<CancellationToken>())).ReturnsAsync(500);
        sites.Setup(r => r.ExistsAsync("taken", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = new SiteService(sites.Object, new CommunityPlanPolicy());

        var result = await service.CreateSiteAsync(owner, new CreateSiteRequest("Site", "taken", null), CancellationToken.None);

        // It got past the plan limit (otherwise the error would be Failure) and stopped at the duplicate key.
        result.ErrorType.Should().Be(ResultErrorType.Conflict);
        sites.Verify(r => r.CountOwnedSitesAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CommunityPolicy_GivesHeatmapsToAnyOwner_WithoutAnyTier()
    {
        var owner = new UserId(Guid.NewGuid());
        var user = new UserId(Guid.NewGuid());
        var site = new AnalyticsSite { Id = SiteId.New(), SiteKey = "tienda", Name = "tienda.es", AllowedOrigins = [], OwnerId = owner };
        var sites = new Mock<ISiteRepository>();
        sites.Setup(s => s.GetSiteByKeyAsync("tienda", It.IsAny<CancellationToken>())).ReturnsAsync(site);
        var access = new Mock<ISiteAccessService>();
        access.Setup(a => a.CanAccessAsync(user, site.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var tokens = new HeatmapOverlayTokenService(new EphemeralDataProtectionProvider(), TimeProvider.System);
        var service = new HeatmapService(Mock.Of<IHeatmapRepository>(), sites.Object, access.Object, new CommunityPlanPolicy(), tokens);

        var result = await service.IssueOverlayTokenAsync(user, new HeatmapOverlayTokenRequest("tienda", "/precios"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task NullBilling_AndCommunityPolicy_NeedNoDatabase()
    {
        var user = UserId.New();

        (await new NullBillingProvider().GetInvoicesAsync(user, CancellationToken.None)).Should().BeEmpty();
        (await new NullBillingProvider().GetPortalUrlAsync(user, CancellationToken.None)).Should().BeNull();
        (await new CommunityPlanPolicy().MaxSitesAsync(user, CancellationToken.None)).Should().BeNull();
    }
}
