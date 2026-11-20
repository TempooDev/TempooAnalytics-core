using AwesomeAssertions;
using Xunit;

namespace TempooAnalytics.Gateway.Tests;

/// <summary>
/// CORS of the core gateway: credentialed answers only for an allow-list (own origin, development hosts, the operator's
/// Cors:AllowedOrigins, origins an overlay contributes); the routes meant for any site are open but never credentialed.
/// Hosts here are fake: the open core names no production host.
/// </summary>
public sealed class GatewayCorsTests : IAsyncLifetime
{
    private const string Evil = "https://evil.example";
    private readonly GatewayHarness _gateway = new(new()
    {
        ["Cors:AllowedOrigins:0"] = "https://operator.example/",
        ["Cors:AllowedOrigins:1"] = "*",
        ["Cors:AllowedOrigins:2"] = "not an origin",
        ["Cors:OverlayOrigins:0"] = "https://overlay.example",
    });

    public Task InitializeAsync() => _gateway.InitializeAsync();
    public Task DisposeAsync() => _gateway.DisposeAsync();

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/me")]
    [InlineData("/api/public/tiers")]
    [InlineData("/admin/users")]
    [InlineData("/dashboard")]
    public async Task UnknownOrigin_IsNotAllowed_OnCredentialedRoutes(string path)
    {
        (await _gateway.Preflight(path, Evil)).ShouldNotAllowOrigin();
        (await _gateway.Send(HttpMethod.Get, path, Evil)).Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public async Task WildcardOrMalformedConfiguredOrigins_DoNotOpenTheDefaultPolicy()
    {
        (await _gateway.Preflight("/api/auth/login", "https://anything.example")).ShouldNotAllowOrigin();
        (await _gateway.Preflight("/api/auth/login", "*")).ShouldNotAllowOrigin();
        (await _gateway.Preflight("/api/auth/login", "not an origin")).ShouldNotAllowOrigin();
    }

    [Theory]
    [InlineData("https://operator.example")]
    [InlineData("https://overlay.example")]
    [InlineData("http://localhost:4200")]
    public async Task ConfiguredOrigins_AreAllowedWithCredentials(string origin)
    {
        (await _gateway.Preflight("/api/auth/login", origin)).ShouldAllowWithCredentials(origin);
    }

    [Fact]
    public async Task OwnOrigin_IsAllowedByDefault_AndOnlyThatOne()
    {
        // The instance answers on its own host: no configuration needed for its own origin.
        (await _gateway.Preflight("/api/auth/login", "http://my-instance.example", host: "my-instance.example"))
            .ShouldAllowWithCredentials("http://my-instance.example");
        // The same instance does not trust a different origin, nor its own host on another scheme.
        (await _gateway.Preflight("/api/auth/login", Evil, host: "my-instance.example")).ShouldNotAllowOrigin();
        (await _gateway.Preflight("/api/auth/login", "https://my-instance.example", host: "my-instance.example")).ShouldNotAllowOrigin();
    }

    [Fact]
    public async Task ActualRequest_FromAllowedOrigin_HasASingleAllowOrigin_EvenIfTheUpstreamAddsItsOwn()
    {
        var response = await _gateway.Send(HttpMethod.Get, "/api/features", "https://operator.example");
        response.ShouldAllowWithCredentials("https://operator.example");
    }

    [Theory]
    [InlineData("https://any-customer-site.example")]
    [InlineData(Evil)]
    public async Task IngestionRoute_IsOpenToAnySite_WithoutCredentials(string origin)
    {
        var preflight = await _gateway.Preflight("/analytics/events", origin);
        preflight.ShouldAllowWithoutCredentials();
        preflight.Headers.GetValues("Access-Control-Allow-Headers").Single().Should().ContainEquivalentOf("authorization");

        // The tracker's real request: the ingestion service answers behind the gateway; the result still carries no credentials.
        var post = await _gateway.Send(HttpMethod.Post, "/analytics/events", origin);
        post.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
        post.Headers.GetValues("Access-Control-Allow-Origin").Should().HaveCount(1);
    }

    [Fact]
    public async Task HeatmapOverlay_IsOpenToAnySite_WithoutCredentials_ButTheRestOfPublicApiIsNot()
    {
        (await _gateway.Preflight("/api/public/heatmaps/overlay", "https://customer-site.example", method: "GET")).ShouldAllowWithoutCredentials();
        (await _gateway.Preflight("/api/analytics/heatmaps/overlay-token", "https://customer-site.example")).ShouldNotAllowOrigin();
    }
}
