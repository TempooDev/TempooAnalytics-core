using TempooAnalytics.Api.Features.Plans;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Features.Billing;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Email;
using TempooAnalytics.Api.Infrastructure.Modules;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

/// <summary>Community edition (no plans, no Resend): data is kept unless configured, and forgot-password fails in a controlled way.</summary>
public class CommunityRetentionAndEmailTests : FunctionalTestBase
{
    public CommunityRetentionAndEmailTests(PostgresFixture fixture) : base(fixture) { }

    private sealed class CloudStubModule : IBillingEndpointsModule
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
        public void MapEndpoints(IEndpointRouteBuilder app) { }
    }

    private WebApplicationFactory<Program> CommunityHost() =>
        Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting(FeatureModuleLoader.EnabledSetting, "false");
            b.UseSetting("Resend:ApiKey", "");
            b.UseSetting("RESEND_APIKEY", "");
        });

    [Fact]
    public async Task Community_WithoutResend_HostStartsAndForgotPasswordFailsUniformlyWithoutTouchingThePassword()
    {
        using var factory = CommunityHost();
        using var client = factory.CreateClient();
        (await client.GetAsync("/api/healthz")).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IEmailService>().IsConfigured.Should().BeFalse();
            await scope.ServiceProvider.GetRequiredService<IUserRepository>().CreateUserAsync(
                new User { Id = UserId.New(), Username = "owner@example.com", Email = "owner@example.com" }, "OriginalPass1!");
        }

        var known = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "owner@example.com" });
        var unknown = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "nobody@example.com" });

        known.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // No account enumeration: same status and body for an existing and a non-existing address.
        unknown.StatusCode.Should().Be(known.StatusCode);
        (await unknown.Content.ReadAsStringAsync()).Should().Be(await known.Content.ReadAsStringAsync());

        // The password must not have been replaced by a temporary one nobody can receive.
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "owner@example.com", password = "OriginalPass1!" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static (RetentionMaintenanceWorker Worker, Mock<IClickHouseRepository> Ch, Mock<IUserPlanStore> Plans) Build(bool community, string? months)
    {
        var users = new Mock<IUserRepository>();
        var plans = new Mock<IUserPlanStore>();
        var ch = new Mock<IClickHouseRepository>();
        var uid = UserId.New();
        users.Setup(u => u.GetAllUsersAsync()).ReturnsAsync(new List<User> { new() { Id = uid, Username = "u" } });
        users.Setup(u => u.ListSitesForUserAsync(uid)).ReturnsAsync(new[] { SiteId.New() });
        plans.Setup(p => p.GetPlanAsync(uid)).ReturnsAsync(new UserPlan("free", 3));

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            months is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { [RetentionMaintenanceWorker.CommunityRetentionSetting] = months }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(cfg)
            .AddScoped(_ => users.Object)
            .AddScoped(_ => plans.Object)
            .AddScoped(_ => ch.Object);
        if (!community) services.AddSingleton(new FeatureModuleCatalog(new IFeatureModule[] { new CloudStubModule() }));
        var sp = services.BuildServiceProvider();
        return (new RetentionMaintenanceWorker(sp, NullLogger<RetentionMaintenanceWorker>.Instance), ch, plans);
    }

    [Fact]
    public async Task Community_RetentionWorker_WithNoSetting_DeletesNothing()
    {
        var (worker, ch, plans) = Build(community: true, months: null);

        await worker.PerformCleanupAsync(CancellationToken.None);

        ch.Verify(c => c.CleanupOldDataAsync(It.IsAny<SiteId>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        plans.Verify(p => p.GetPlanAsync(It.IsAny<UserId>()), Times.Never);
    }

    [Fact]
    public async Task Community_RetentionWorker_WithConfiguredMonths_UsesThemInsteadOfTheFreeTier()
    {
        var (worker, ch, _) = Build(community: true, months: "24");
        DateTime? cutoff = null;
        ch.Setup(c => c.CleanupOldDataAsync(It.IsAny<SiteId>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
          .Callback<SiteId, DateTime, CancellationToken>((_, d, _) => cutoff = d).ReturnsAsync(0);

        await worker.PerformCleanupAsync(CancellationToken.None);

        cutoff.Should().NotBeNull();
        cutoff!.Value.Should().BeCloseTo(DateTime.UtcNow.AddMonths(-24), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Cloud_RetentionWorker_StillAppliesTheTierRetention()
    {
        var (worker, ch, _) = Build(community: false, months: null);
        DateTime? cutoff = null;
        ch.Setup(c => c.CleanupOldDataAsync(It.IsAny<SiteId>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
          .Callback<SiteId, DateTime, CancellationToken>((_, d, _) => cutoff = d).ReturnsAsync(0);

        await worker.PerformCleanupAsync(CancellationToken.None);

        cutoff.Should().NotBeNull();
        cutoff!.Value.Should().BeCloseTo(DateTime.UtcNow.AddMonths(-3), TimeSpan.FromMinutes(1));
    }
}
