using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TempooAnalytics.Api.Infrastructure.Modules;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

/// <summary>The core allows its own development hosts and the ones the operator lists in <c>Cors:AllowedOrigins</c>; no production host is built in.</summary>
public class CorsConfiguredOriginsIntegrationTests : FunctionalTestBase
{
    public CorsConfiguredOriginsIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    private static async Task<string?> AllowedOrigin(WebApplicationFactory<Program> host, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/features");
        request.Headers.Add("Origin", origin);
        var response = await host.CreateClient().SendAsync(request);
        return response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.FirstOrDefault() : null;
    }

    private WebApplicationFactory<Program> CommunityHost(string? allowed) =>
        Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting(FeatureModuleLoader.EnabledSetting, "false");
            if (allowed != null) b.UseSetting("Cors:AllowedOrigins:0", allowed);
        });

    [Fact]
    public async Task OperatorOrigin_IsAllowed_OnlyWhenConfigured()
    {
        using (var host = CommunityHost("https://stats.example.test"))
            (await AllowedOrigin(host, "https://stats.example.test")).Should().Be("https://stats.example.test");

        using (var host = CommunityHost(null))
            (await AllowedOrigin(host, "https://stats.example.test")).Should().BeNull();
    }

    [Fact]
    public async Task DevelopmentOrigin_IsAllowedWithoutConfiguration()
    {
        using var host = CommunityHost(null);
        (await AllowedOrigin(host, "http://localhost:4200")).Should().Be("http://localhost:4200");
    }
}
