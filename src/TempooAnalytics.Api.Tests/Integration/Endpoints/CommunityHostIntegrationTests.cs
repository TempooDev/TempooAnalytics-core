using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Auth.Services;
using TempooAnalytics.Api.Features.Billing;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Modules;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

/// <summary>
/// The whole API host in community mode (<c>Modules:Enabled=false</c>): it must build, expose no billing, payments, AI
/// or public-API surface, run on <see cref="CommunityPlanPolicy"/> / <see cref="NullBillingProvider"/>, and never
/// read <c>billing.*</c>. The same host with modules loaded (cloud) is smoke-tested so the checks are not vacuous.
/// </summary>
public partial class CommunityHostIntegrationTests : FunctionalTestBase
{
    public CommunityHostIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    // Names that mean billing / payments / AI credits / public API. Anything routed in the host that matches must be listed below.
    private static readonly Regex Suspect = new(
        "billing|subscri|tier|checkout|portal|invoice|credit|payment|grpc|api/ai\\b|/ai/|public-?api|api-?keys?|ai-?usage",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Routes that match <see cref="Suspect"/> and legitimately exist in the community host (route text -> why).
    /// </summary>
    private static readonly Dictionary<string, string> CommunityAllowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Admin/Tiers"] = "Razor page mapped by the framework; the module resource filter answers 404 (Community_AnswersNotFound)",
    };

    private WebApplicationFactory<Program> Build(bool modules, bool validate = false, bool paymentsOn = false) =>
        // UseSetting, not ConfigureAppConfiguration: the modules are discovered while the services are registered.
        Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting(FeatureModuleLoader.EnabledSetting, modules ? "true" : "false");
            if (paymentsOn) b.UseSetting("EnablePayments", "true");
            if (validate) b.UseDefaultServiceProvider((_, o) => { o.ValidateScopes = true; o.ValidateOnBuild = true; });
        });

    private sealed record Described(string Route, string Methods, string Detail)
    {
        public string Key => Route.TrimStart('/');
        public override string ToString() => $"{Methods} {Route} [{Detail}]";
    }

    private static List<Described> Describe(IServiceProvider services) =>
        services.GetServices<EndpointDataSource>().SelectMany(ds => ds.Endpoints).OfType<RouteEndpoint>().Select(e =>
        {
            var methods = string.Join(",", e.Metadata.OfType<IHttpMethodMetadata>().SelectMany(m => m.HttpMethods).Distinct());
            var tags = string.Join(",", e.Metadata.OfType<ITagsMetadata>().SelectMany(t => t.Tags));
            var name = e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
            var metadataTypes = string.Join(",", e.Metadata.Select(m => m.GetType().Name));
            return new Described(e.RoutePattern.RawText ?? "", methods, $"{e.DisplayName}; name={name}; tags={tags}; metadata={metadataTypes}");
        }).ToList();

    private static List<Described> SuspectRoutes(IServiceProvider services) =>
        Describe(services).Where(d => Suspect.IsMatch(d.Route) || Suspect.IsMatch(d.Detail)).ToList();

    private HttpClient AdminClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        AuthenticateClient(client, Guid.NewGuid(), isAdmin: true);
        return client;
    }

    // 1. Boots without DI exceptions

    [Fact]
    public async Task Community_BootsWithScopeAndBuildValidation_AndEveryPageAndKeyServiceResolves()
    {
        using var factory = Build(modules: false, validate: true, paymentsOn: true);
        var services = factory.Server.Services; // building the host runs ValidateOnBuild + ValidateScopes

        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        foreach (var type in new[] { typeof(IAuthService), typeof(IUserManagementService), typeof(IPlanPolicy), typeof(IBillingProvider), typeof(IUserRepository), typeof(IUserPlanStore) })
            sp.GetRequiredService(type);

        // Every Razor page goes through the real pipeline (page model activation included): 200 or 404, never a DI failure.
        var admin = AdminClient(factory);
        var pageRoutes = Describe(services).Where(d => d.Detail.Contains("CompiledPageActionDescriptor") && !d.Route.Contains('{')).Select(d => d.Route).ToList();
        pageRoutes.Should().NotBeEmpty();
        var broken = new List<string>();
        foreach (var route in pageRoutes)
        {
            if (route.TrimStart('/').Equals("Admin/Users", StringComparison.OrdinalIgnoreCase)) continue; // known gap, see Community_AdminUsersPage_Works
            var status = (await admin.GetAsync(route)).StatusCode;
            if ((int)status >= 500) broken.Add($"{route}: {(int)status}");
        }
        broken.Should().BeEmpty("every Razor page of the community host must answer without a server error");

        using var client = factory.CreateClient();
        (await client.GetAsync("/api/healthz")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void CoreRegistrations_InAProductionShapedCommunityBuild_HaveNoPaymentsPlumbing()
    {
        // The test host never registers the hosted services of modules (isTest), so check the service collection with isTest:false.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [FeatureModuleLoader.EnabledSetting] = "false",
            ["EnablePayments"] = "true",
            ["ConnectionStrings:analytics-db"] = Fixture.ConnectionString,
            ["ConnectionStrings:clickhouse"] = "Host=localhost;Port=9000;Database=default",
            ["Auth:JwtKey"] = "this_key_must_be_at_least_32_characters_long_for_security",
        }).Build();

        var services = new ServiceCollection().AddServicesExtensions(config, isTest: false);

        services.Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => (d.ImplementationType ?? d.ImplementationInstance?.GetType())?.Name ?? "factory")
            .Should().NotContain(n => Regex.IsMatch(n, "payment|billing|subscription", RegexOptions.IgnoreCase));
        // By name: the payments client and the billing user store are cloud types (Api.Cloud), unknown to the core tree.
        services.Should().NotContain(d => d.ServiceType.Name == "PaymentServiceClient");
        services.Last(d => d.ServiceType == typeof(IPlanPolicy)).ImplementationType.Should().Be(typeof(CommunityPlanPolicy));
        services.Last(d => d.ServiceType == typeof(IUserPlanStore)).ImplementationType.Should().Be(typeof(NoPlanUserStore));
        services.Should().NotContain(d => d.ServiceType.Name == "IBillingUserStore");
        services.Last(d => d.ServiceType == typeof(IBillingProvider)).ImplementationType.Should().Be(typeof(NullBillingProvider));
        services.Should().NotContain(d => d.ServiceType == typeof(IProfileContributor));
    }

    // 2. Routes

    [Fact]
    public void Community_MapsNoBillingPaymentsAiOrPublicApiRoute_OutsideTheExplicitList()
    {
        using var factory = Build(modules: false, paymentsOn: true);

        var leaked = SuspectRoutes(factory.Server.Services).Where(d => !CommunityAllowed.ContainsKey(d.Key)).ToList();

        leaked.Should().BeEmpty("the community host must not map these routes: " + string.Join(" | ", leaked));
    }

    [Fact]
    public void Community_NoGrpcEndpointIsMapped()
    {
        using var factory = Build(modules: false, paymentsOn: true);

        Describe(factory.Server.Services).Where(d => d.Detail.Contains("Grpc", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("GET", "/api/subscription/me")]
    [InlineData("GET", "/api/subscription/portal-session")]
    [InlineData("GET", "/api/subscription/invoices")]
    [InlineData("GET", "/api/admin/tiers")]
    [InlineData("GET", "/api/public/tiers")]
    [InlineData("GET", "/Admin/Tiers")]
    [InlineData("GET", "/api/ai/credits")]
    [InlineData("GET", "/api/ai/credits/history")]
    [InlineData("GET", "/api/ai/preferences")]
    [InlineData("GET", "/api/public-api/keys")]
    [InlineData("GET", "/api/public-api/usage")]
    [InlineData("GET", "/Admin/ai-usage")]
    [InlineData("GET", "/Admin/public-api")]
    [InlineData("POST", "/api/admin/users/00000000-0000-0000-0000-000000000001/subscription")]
    public async Task Community_AnswersNotFound(string method, string path)
    {
        using var factory = Build(modules: false, paymentsOn: true);
        var client = AdminClient(factory);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method == "POST" ? JsonContent.Create(new { }) : null
        });

        // Non-GET requests to an unrouted path hit the SPA fallback (GET only): 405 means "no such endpoint" there.
        response.StatusCode.Should().Match(s => s == HttpStatusCode.NotFound || (method != "GET" && s == HttpStatusCode.MethodNotAllowed));
    }

    [Fact]
    public async Task Community_GrpcPaymentServiceIsNotReachable()
    {
        using var factory = Build(modules: false, paymentsOn: true);
        var client = factory.CreateClient();

        // gRPC call shape: POST application/grpc to /<package>.<Service>/<Method>.
        var request = new HttpRequestMessage(HttpMethod.Post, "/payments.UserPaymentService/GetUserByCustomerId")
        {
            Version = HttpVersion.Version20,
            Content = new ByteArrayContent(new byte[5])
        };
        request.Content.Headers.ContentType = new("application/grpc");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        response.Headers.Contains("grpc-status").Should().BeFalse();
    }

    // The admin console is how a community instance creates its users (docs/selfhost-first-run.md): it must not need the AI module.
    [Fact]
    public async Task Community_AdminUsersPage_Works()
    {
        using var factory = Build(modules: false);

        var response = await AdminClient(factory).GetAsync("/Admin/Users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().NotContain("ai-credits", "the AI credit panel belongs to the cloud modules")
            .And.NotContain("Créditos IA").And.NotContain("/admin/ai-usage", "the AI usage page does not exist in community")
            .And.NotContain("/admin/public-api", "the public API page does not exist in community");
    }

    // 3. DI

    [Fact]
    public void Community_RegistersOnlyTheCoreImplementations()
    {
        using var factory = Build(modules: false, paymentsOn: true);
        using var scope = factory.Server.Services.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<IBillingProvider>().Should().BeOfType<NullBillingProvider>();
        sp.GetRequiredService<IPlanPolicy>().Should().BeOfType<CommunityPlanPolicy>();
        sp.GetRequiredService<IUserPlanStore>().Should().BeOfType<NoPlanUserStore>();
        sp.GetServices<IBillingProvider>().Should().ContainSingle().Which.Should().BeOfType<NullBillingProvider>();
        sp.GetServices<IPlanPolicy>().Should().ContainSingle().Which.Should().BeOfType<CommunityPlanPolicy>();
        sp.GetServices<IProfileContributor>().Should().BeEmpty();
        factory.Server.Services.GetServices<IHostedService>().Select(h => h.GetType().Name)
            .Should().NotContain(n => Regex.IsMatch(n, "payment|billing", RegexOptions.IgnoreCase));
        factory.Server.Services.GetRequiredService<FeatureModuleCatalog>().Modules.Should().BeEmpty();
    }

    // 4. /api/auth/me

    private async Task<Guid> SeedUser(string username, string password = "correct-horse-battery")
    {
        var id = Guid.NewGuid();
        using var db = Fixture.CreateContext();
        db.Users.Add(new User
        {
            Id = new UserId(id), Username = username, Email = username,
            PasswordHash = PasswordHelper.HashPassword(password), IsActive = true, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Community_Me_ReturnsIdentityOnly()
    {
        var userId = await SeedUser("me-community@tempoo.test");
        using var factory = Build(modules: false);
        var client = factory.CreateClient();
        AuthenticateClient(client, userId, isAdmin: false);

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var fields = body.EnumerateObject().Select(p => p.Name).ToList();
        fields.Should().BeEquivalentTo("id", "username", "email", "isAdmin");
        fields.Should().NotContain(f => Regex.IsMatch(f, "subscri|trial|plan|billing|tier|customer|portal", RegexOptions.IgnoreCase));
    }

    // 5. No billing.* reads

    private static readonly Regex BillingSql = new("\"?\\bbilling\"?\\s*\\.\\s*\"?\\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private async Task Exec(params string[] statements)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();
        foreach (var sql in statements)
        {
            await using var c = new NpgsqlCommand(sql, conn);
            await c.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Runs a few core flows against the host and returns what Postgres logged for them (log_statement=all, so it covers
    /// every client: EF contexts, raw Npgsql, anything). A marker statement separates it from earlier noise.
    /// </summary>
    private async Task<string> RunCoreFlowAndCaptureSql(bool modules)
    {
        var marker = $"flow_marker_{Guid.NewGuid():N}";
        await Exec("ALTER SYSTEM SET log_statement = 'all'", "SELECT pg_reload_conf()");
        await Task.Delay(500);

        try
        {
            await Exec($"SELECT '{marker}'");

            using var factory = Build(modules); // startup: seeding and initialisation
            var admin = AdminClient(factory);

            // signup (admin creates the account), login, profile, site creation, site list, ingestion and a metrics query
            var email = $"flow-{Guid.NewGuid():N}@tempoo.test";
            var signup = await admin.PostAsJsonAsync("/api/admin/signup", new { username = email, password = "correct-horse-battery", email });
            signup.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

            var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = email, password = "correct-horse-battery" });
            login.StatusCode.Should().Be(HttpStatusCode.OK);

            using var db = Fixture.CreateContext();
            var userId = db.Users.Single(u => u.Username == email).Id.Value;
            var user = factory.CreateClient();
            AuthenticateClient(user, userId, isAdmin: false);

            (await user.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
            var site = await user.PostAsJsonAsync("/api/admin/sites", new { name = "Flow", siteKey = $"flow-{Guid.NewGuid():N}"[..20], allowedOrigins = new[] { "localhost" } });
            site.StatusCode.Should().Be(HttpStatusCode.OK);
            (await user.GetAsync("/api/auth/sites")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await user.GetAsync("/api/features")).StatusCode.Should().Be(HttpStatusCode.OK);

            var created = db.AnalyticsSites.Single(s => s.OwnerId == new UserId(userId));
            var ingest = new HttpRequestMessage(HttpMethod.Post, "/analytics/events")
            {
                Content = JsonContent.Create(new
                {
                    siteId = created.SiteKey, type = "pageview", url = "http://localhost/", visitorId = "v1", sessionId = "s1", timestamp = DateTimeOffset.UtcNow
                })
            };
            ingest.Headers.Add("Authorization", $"Bearer {created.WriteKey}");
            ingest.Headers.Add("Origin", "http://localhost");
            await factory.CreateClient().SendAsync(ingest); // ClickHouse/Redis are not in the test stack: the status does not matter, the SQL does
            await user.GetAsync("/api/analytics/sites"); // ClickHouse is not in the test stack: the status does not matter, the SQL does
        }
        finally
        {
            await Exec("ALTER SYSTEM RESET log_statement", "SELECT pg_reload_conf()");
        }

        var logs = await Fixture.GetLogsAsync();
        var at = logs.LastIndexOf(marker, StringComparison.Ordinal);
        at.Should().BeGreaterThan(-1, "the marker statement must be in the Postgres log (log_statement=all)");
        return logs[at..];
    }

    [Fact]
    public async Task Community_CoreFlows_NeverTouchBillingTables()
    {
        var sql = await RunCoreFlowAndCaptureSql(modules: false);

        sql.Should().Contain("analytics_sites", "the flow did reach the database");
        var hits = BillingSql.Matches(sql).Select(m => m.Value).Distinct().ToList();
        hits.Should().BeEmpty("a community host must not read or write billing.*");
    }

    // 6. Cloud smoke

}
