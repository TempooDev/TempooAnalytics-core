using Microsoft.Extensions.Configuration.Json;
using TempooAnalytics.Gateway;
using TempooAnalytics.ServiceDefaults;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        // The client IP (X-Forwarded-For) is resolved by UseCloudflareClientIp, which only trusts
        // Cloudflare's headers when the request came from a Cloudflare edge address.
        options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
            | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.ForwardLimit = null; // Traefik is the only hop in front of the gateway
    });

    // Optional overlay: extra routes and clusters for additional upstream services live in
    // appsettings.Cloud.json, which is absent in a plain deployment. It goes right after the appsettings*.json
    // files and BEFORE environment variables / command line, so the env overrides of the production compose
    // keep winning exactly as they did when these entries lived in appsettings.json. Missing file = core.
    var cloudOverlay = new JsonConfigurationSource { Path = "appsettings.Cloud.json", Optional = true, ReloadOnChange = true };
    var lastAppSettings = builder.Configuration.Sources
        .Select((source, index) => (source, index))
        .Where(x => x.source is JsonConfigurationSource { Path: not null } json
            && Path.GetFileName(json.Path).StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase))
        .Select(x => x.index)
        .DefaultIfEmpty(-1)
        .Max();
    builder.Configuration.Sources.Insert(lastAppSettings + 1, cloudOverlay);

    builder.AddServiceDefaults();

    // CORS: credentialed only for an allow-list of origins by default; open and credential-less for the routes meant for any site
    // (see GatewayCors). Overlay origins come from appsettings.Cloud.json (Cors:OverlayOrigins), the operator's from Cors:AllowedOrigins.
    builder.Services.AddGatewayCors(builder.Configuration);

    builder.Services.AddReverseProxy()
        .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
        .AddServiceDiscoveryDestinationResolver();

    var app = builder.Build();

    app.MapDefaultEndpoints();

    // Must be before other middleware that depends on IP/Proto/Host
    app.UseCloudflareClientIp();
    app.UseForwardedHeaders();

    app.UseCors();

    app.MapReverseProxy();

    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"Gateway terminated unexpectedly: {ex}");
    throw;
}
