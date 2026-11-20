using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TempooAnalytics.Gateway.Tests;

/// <summary>
/// The gateway with every cluster pointed at a stub that answers with its own name. The <c>api</c> and <c>ingestion</c> stubs add
/// their own <c>Access-Control-Allow-Origin</c> to whatever origin they are called with, as the API's AllowFrontend policy and the
/// ingestion service do, so a test sees what the browser would receive from gateway + upstream together.
/// </summary>
public sealed class GatewayHarness : IAsyncLifetime
{
    private readonly Dictionary<string, WebApplication> _stubs = new();
    private readonly Dictionary<string, string?> _extraSettings;
    private WebApplicationFactory<Program> _gateway = null!;

    private readonly string[] _clusters;

    /// <param name="extraClusters">Stub upstreams to start besides the core's (api, web, ingestion).</param>
    public GatewayHarness(Dictionary<string, string?>? extraSettings = null, params string[] extraClusters)
    {
        _extraSettings = extraSettings ?? new();
        _clusters = new[] { "api", "web", "ingestion" }.Concat(extraClusters).ToArray();
    }

    public async Task InitializeAsync()
    {
        var settings = new Dictionary<string, string?>(_extraSettings);
        foreach (var name in _clusters)
        {
            var stub = WebApplication.CreateBuilder();
            stub.WebHost.UseUrls("http://127.0.0.1:0");
            var app = stub.Build();
            app.Run(context =>
            {
                if (name is "api" or "ingestion" && context.Request.Headers.Origin.Count > 0)
                    context.Response.Headers.AccessControlAllowOrigin = context.Request.Headers.Origin.ToString();
                return context.Response.WriteAsync(name);
            });
            await app.StartAsync();
            _stubs[name] = app;
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            settings[$"ReverseProxy:Clusters:{name}-cluster:Destinations:destination1:Address"] = address;
        }

        _gateway = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
        });
    }

    public Task<HttpResponseMessage> Preflight(string path, string origin, string? host = null, string method = "POST")
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        if (host != null) request.Headers.Host = host;
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");
        return _gateway.CreateClient().SendAsync(request);
    }

    public Task<HttpResponseMessage> Send(HttpMethod method, string path, string origin, string? host = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (host != null) request.Headers.Host = host;
        request.Headers.TryAddWithoutValidation("Origin", origin);
        return _gateway.CreateClient().SendAsync(request);
    }

    public async Task DisposeAsync()
    {
        _gateway.Dispose();
        foreach (var stub in _stubs.Values) await stub.DisposeAsync();
    }
}

public static class CorsAssertions
{
    public static void ShouldAllowWithCredentials(this HttpResponseMessage response, string origin)
    {
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(origin);
        response.Headers.GetValues("Access-Control-Allow-Credentials").Should().Equal("true");
    }

    public static void ShouldAllowWithoutCredentials(this HttpResponseMessage response)
    {
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("*");
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    public static void ShouldNotAllowOrigin(this HttpResponseMessage response)
    {
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }
}
