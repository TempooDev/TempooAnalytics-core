using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Imports.Models;
using TempooAnalytics.Api.Features.Imports.Services;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Modules;
using TempooAnalytics.Api.Infrastructure.Services;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

/// <summary>
/// Community (<c>Modules:Enabled=false</c>) has no plans and no limits: import, export and site creation work for any
/// signed-in user, with the real <see cref="NoPlanUserStore"/> and <see cref="CommunityPlanPolicy"/> (nothing mocked
/// about plans). The same requests on the cloud host stay denied for a user without a paid plan, so the community
/// answers are not an accident of the test setup.
/// </summary>
public partial class CommunityNoPlanLimitsIntegrationTests : FunctionalTestBase
{
    private readonly Mock<IStorageService> _storage = new();
    private readonly Mock<IImportService> _imports = new();

    public CommunityNoPlanLimitsIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _storage.Setup(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>())).ReturnsAsync("ok");
        _imports.Setup(s => s.EnqueueCsvImport(It.IsAny<SiteId>(), It.IsAny<string>(), ImportType.Plausible, It.IsAny<UserId>())).ReturnsAsync(Guid.NewGuid());
    }

    private WebApplicationFactory<Program> Build(bool modules) =>
        Factory.WithWebHostBuilder(b =>
        {
            // UseSetting, not ConfigureAppConfiguration: the modules are discovered while the services are registered.
            b.UseSetting(FeatureModuleLoader.EnabledSetting, modules ? "true" : "false");
            b.ConfigureAppConfiguration((_, conf) => conf.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:PlausibleImport"] = "true"
            }));
            b.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => _storage.Object);
                services.AddScoped(_ => _imports.Object);
            });
        });

    private async Task<(Guid UserId, Guid SiteId)> SeedUserWithSite(string name)
    {
        var userId = Guid.NewGuid();
        var siteId = SiteId.New();
        using var db = Fixture.CreateContext();
        db.Users.Add(new User
        {
            Id = new UserId(userId), Username = name, Email = $"{name}@example.com",
            PasswordHash = PasswordHelper.HashPassword("correct-horse-battery"), IsActive = true, CreatedAt = DateTime.UtcNow
        });
        db.AnalyticsSites.Add(new AnalyticsSite
        {
            Id = siteId, SiteKey = name, Name = name, OwnerId = new UserId(userId), CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (userId, siteId.Value);
    }

    private HttpClient SignedIn(WebApplicationFactory<Program> factory, Guid userId)
    {
        var client = factory.CreateClient();
        AuthenticateClient(client, userId, false);
        return client;
    }

    private static MultipartFormDataContent Upload(string fileName)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[128]), "file", fileName);
        return content;
    }

    private static object ExportRequest() => new { from = DateTime.UtcNow.AddDays(-7), to = DateTime.UtcNow };

    [Fact]
    public async Task Community_PlanStore_GrantsTheCommunityPlan_AndPlanPolicyIsUnlimited()
    {
        using var factory = Build(modules: false);
        using var scope = factory.Services.CreateScope();
        var user = UserId.New();

        var plan = await scope.ServiceProvider.GetRequiredService<IUserPlanStore>().GetPlanAsync(user);
        plan.Should().NotBeNull();
        plan!.Slug.Should().NotBe("free");
        var policy = scope.ServiceProvider.GetRequiredService<IPlanPolicy>();
        (await policy.MaxSitesAsync(user, CancellationToken.None)).Should().BeNull();
        (await policy.CanCreateSiteAsync(user, CancellationToken.None)).Should().BeTrue();
        (await policy.HasAdvancedAnalyticsAsync(user, CancellationToken.None)).Should().BeTrue();
    }

    [Theory]
    [InlineData("export.zip")]
    [InlineData("daily.csv")]
    public async Task Community_PlausibleImportUpload_IsAccepted(string fileName)
    {
        var (userId, siteId) = await SeedUserWithSite($"cm-import-{Guid.NewGuid():N}");
        using var factory = Build(modules: false);

        var response = await SignedIn(factory, userId).PostAsync($"/api/sites/{siteId}/imports/plausible", Upload(fileName));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        _imports.Verify(s => s.EnqueueCsvImport(new SiteId(siteId), It.IsAny<string>(), ImportType.Plausible, new UserId(userId)), Times.Once);
    }

    [Fact]
    public async Task Community_Ga4Authorize_IsNotDeniedByPlan()
    {
        var (userId, siteId) = await SeedUserWithSite($"cm-ga4-{Guid.NewGuid():N}");
        using var factory = Build(modules: false);

        var response = await SignedIn(factory, userId).PostAsync($"/api/sites/{siteId}/imports/ga4/authorize", null);

        // GA4 needs a Google OAuth client the test does not have: anything but the plan's 403 is fine.
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Community_ExportRequest_IsAccepted_AndRegistersAJob()
    {
        var (userId, siteId) = await SeedUserWithSite($"cm-export-{Guid.NewGuid():N}");
        using var factory = Build(modules: false);

        var response = await SignedIn(factory, userId).PostAsJsonAsync($"/api/sites/{siteId}/export", ExportRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var db = Fixture.CreateContext();
        db.ExportJobs.Any(j => j.SiteId == new SiteId(siteId)).Should().BeTrue();
    }

    [Fact]
    public async Task Community_SiteCreation_HasNoLimit()
    {
        var (userId, _) = await SeedUserWithSite($"cm-sites-{Guid.NewGuid():N}");
        using var factory = Build(modules: false);
        var client = SignedIn(factory, userId);

        // The user already owns one site; the cloud's free plan stops there.
        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/admin/sites", new CreateSiteRequest($"Extra {i}", $"cm-extra-{Guid.NewGuid():N}", null));
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }
    }

}
