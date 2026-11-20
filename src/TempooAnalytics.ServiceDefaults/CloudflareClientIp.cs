using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace TempooAnalytics.ServiceDefaults;

// Decides who the real client is at the edge of the app, so services behind the gateway can trust
// CF-Connecting-IP / CF-IPCountry. Those headers are only honoured when the hop that reached Traefik
// is a Cloudflare edge address; otherwise anyone hitting the origin directly could pick their own IP
// (bypassing per-IP rate limits) or their own country and visitor hash (polluting analytics).
public static class CloudflareClientIp
{
    // https://www.cloudflare.com/ips/ — override with Cloudflare:TrustedRanges if the list changes.
    public static readonly string[] DefaultRanges =
    [
        "173.245.48.0/20", "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22",
        "141.101.64.0/18", "108.162.192.0/18", "190.93.240.0/20", "188.114.96.0/20",
        "197.234.240.0/22", "198.41.128.0/17", "162.158.0.0/15", "104.16.0.0/13",
        "104.24.0.0/14", "172.64.0.0/13", "131.0.72.0/22",
        "2400:cb00::/32", "2606:4700::/32", "2803:f800::/32", "2405:b500::/32",
        "2405:8100::/32", "2a06:98c0::/29", "2c0f:f248::/32",
    ];

    // Set by Cloudflare: trusted only when the request really came through Cloudflare.
    private static readonly string[] CloudflareHeaders = ["CF-Connecting-IP", "CF-Connecting-IPv6", "CF-IPCountry"];

    // Never set by our edge, but read as fallbacks downstream: always client-controlled, always dropped.
    private static readonly string[] UntrustedHeaders = ["True-Client-IP", "X-Geoip-Country", "X-Country"];

    public static IReadOnlyList<IPNetwork> ParseRanges(IEnumerable<string> ranges) =>
        ranges.Select(r => IPNetwork.Parse(r.Trim())).ToList();

    // The address that opened the connection to our reverse proxy (Traefik). Traefik appends it as the
    // last X-Forwarded-For entry whether or not it kept the client's own entries, so the rightmost
    // parseable entry is the only one a client cannot forge.
    public static IPAddress? GetEdgePeer(IHeaderDictionary headers, IPAddress? connectionIp)
    {
        var entries = headers["X-Forwarded-For"]
            .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();

        for (var i = entries.Length - 1; i >= 0; i--)
        {
            if (IPAddress.TryParse(entries[i], out var ip)) return Normalize(ip);
        }

        return connectionIp is null ? null : Normalize(connectionIp);
    }

    public static bool IsCloudflare(IPAddress? ip, IReadOnlyList<IPNetwork> ranges)
    {
        if (ip is null) return false;
        ip = Normalize(ip);
        foreach (var range in ranges)
        {
            if (range.Contains(ip)) return true;
        }
        return false;
    }

    // Rewrites the request so every downstream service sees one consistent, trustworthy client IP:
    // RemoteIpAddress and X-Forwarded-For carry the client, and CF-* headers survive only from Cloudflare.
    public static IPAddress? Apply(HttpContext context, IReadOnlyList<IPNetwork> ranges)
    {
        var headers = context.Request.Headers;
        var peer = GetEdgePeer(headers, context.Connection.RemoteIpAddress);
        var client = peer;

        foreach (var name in UntrustedHeaders) headers.Remove(name);

        if (IsCloudflare(peer, ranges)
            && IPAddress.TryParse(headers["CF-Connecting-IP"].FirstOrDefault()?.Trim(), out var cfIp))
        {
            client = Normalize(cfIp);
            headers["CF-Connecting-IP"] = client.ToString();
        }
        else
        {
            foreach (var name in CloudflareHeaders) headers.Remove(name);
        }

        if (client is not null)
        {
            context.Connection.RemoteIpAddress = client;
            headers["X-Forwarded-For"] = new StringValues(client.ToString());
        }

        return client;
    }

    // Must run before UseForwardedHeaders and before the reverse proxy.
    public static IApplicationBuilder UseCloudflareClientIp(this IApplicationBuilder app)
    {
        var config = app.ApplicationServices.GetRequiredService<IConfiguration>();
        var configured = config.GetSection("Cloudflare:TrustedRanges").Get<string[]>();
        var ranges = ParseRanges(configured is { Length: > 0 } ? configured : DefaultRanges);

        return app.Use((context, next) =>
        {
            Apply(context, ranges);
            return next(context);
        });
    }

    private static IPAddress Normalize(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
}
