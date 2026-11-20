using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace TempooAnalytics.Gateway;

/// <summary>
/// CORS of the gateway. Two kinds of route:
/// <list type="bullet">
/// <item><b>Default policy</b> (every route without an explicit <c>CorsPolicy</c>: the web app's API calls, the admin console...):
/// credentialed, but only for an allow-list of origins. Never "any origin": reflecting an arbitrary origin together with
/// <c>Access-Control-Allow-Credentials</c> would let any site read authenticated responses on behalf of a logged-in user.</item>
/// <item><b>Open policies</b> (<see cref="OpenNoCredentials"/>, <see cref="AnalyticsCors"/>) for routes meant to be called from any
/// site (the tracker's <c>/analytics/events</c>, the heatmap overlay, the public API): any origin, <b>never</b> credentials.
/// These routes authenticate with a key or token in the URL or an <c>Authorization</c> header, not with cookies.</item>
/// </list>
/// Origins of the default policy: the development hosts, the instance's own origin (derived from each request, see
/// <see cref="OwnOriginCorsPolicyProvider"/>), the operator's <c>Cors:AllowedOrigins</c> and the origins a configuration overlay
/// contributes through <c>Cors:OverlayOrigins</c> (the hosted service's appsettings.Cloud.json), so the open core names no production host.
/// </summary>
public static class GatewayCors
{
    /// <summary>Routes called from arbitrary sites: any origin, no credentials.</summary>
    public const string OpenNoCredentials = "OpenNoCredentials";

    /// <summary>Historical name of the ingestion route's policy (deployments set it through <c>ReverseProxy:Routes:ingestion-route:CorsPolicy</c>); same as <see cref="OpenNoCredentials"/>.</summary>
    public const string AnalyticsCors = "AnalyticsCors";

    /// <summary>Same development hosts as the API's <c>AllowFrontend</c> policy (Angular dev servers, Astro, Aspire).</summary>
    private static readonly string[] DevOrigins =
    [
        "http://localhost:4200",
        "http://localhost:4300",
        "http://localhost:4321",
        "https://localhost:60820",
        "http://localhost:60822",
    ];

    public static IServiceCollection AddGatewayCors(this IServiceCollection services, IConfiguration configuration)
    {
        var configured = DevOrigins
            .Concat(Normalize(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()))
            .Concat(Normalize(configuration.GetSection("Cors:OverlayOrigins").Get<string[]>()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy => policy
                .WithOrigins(configured)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());

            foreach (var name in new[] { OpenNoCredentials, AnalyticsCors })
            {
                options.AddPolicy(name, policy => policy
                    .AllowAnyOrigin()
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    // The tracker reads when to retry; browser MCP clients need the session id.
                    .WithExposedHeaders("Retry-After", "Mcp-Session-Id", "WWW-Authenticate"));
            }
        });

        services.Replace(ServiceDescriptor.Singleton<ICorsPolicyProvider, OwnOriginCorsPolicyProvider>());
        return services;
    }

    /// <summary>Keeps absolute http(s) origins (scheme://host[:port]); drops blanks, "*" and anything malformed.</summary>
    internal static IEnumerable<string> Normalize(IEnumerable<string>? origins)
    {
        foreach (var raw in origins ?? [])
        {
            if (Uri.TryCreate(raw?.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                yield return uri.GetLeftPart(UriPartial.Authority);
        }
    }
}

/// <summary>
/// Adds the instance's own origin (<c>scheme://host</c> of the request, after the forwarded headers) to the default policy, so a
/// fresh install works without listing its own address. Browsers do not need CORS for same-origin calls; this covers the case where
/// the instance's web app and API are reached through different origins that share this gateway. The origin is only compared with
/// the request's <c>Origin</c> header, which a page cannot forge; a forged <c>X-Forwarded-Host</c> cannot help an attacker because
/// the browser sends the preflight without it.
/// </summary>
internal sealed class OwnOriginCorsPolicyProvider(IOptions<CorsOptions> options) : ICorsPolicyProvider
{
    private readonly DefaultCorsPolicyProvider _inner = new(options);

    public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        var policy = await _inner.GetPolicyAsync(context, policyName);
        if (policy is null || (policyName is not null && policyName != options.Value.DefaultPolicyName)) return policy;

        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length == 0 || policy.Origins.Contains(origin, StringComparer.OrdinalIgnoreCase)) return policy;

        var own = $"{context.Request.Scheme}://{context.Request.Host}";
        if (!string.Equals(origin, own, StringComparison.OrdinalIgnoreCase)) return policy;

        // A new policy, not a copy of the default one: a copy keeps the origin predicate of the original.
        return new CorsPolicyBuilder().WithOrigins(origin).AllowAnyHeader().AllowAnyMethod().AllowCredentials().Build();
    }
}
