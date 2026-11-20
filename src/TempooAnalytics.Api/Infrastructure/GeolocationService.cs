using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;

namespace TempooAnalytics.Api.Infrastructure;

public interface IGeolocationService
{
    string? GetCountryCode(HttpRequest request);
    string GetVisitorId(HttpRequest request);
}

public class GeolocationService : IGeolocationService
{
    private readonly GeoIpDatabase _database;
    private readonly ILogger<GeolocationService> _logger;

    /// <summary>Production constructor: the reader may be absent (no database) or swapped by the provisioner.</summary>
    [ActivatorUtilitiesConstructor]
    public GeolocationService(GeoIpDatabase database, ILogger<GeolocationService> logger)
    {
        _database = database;
        _logger = logger;
    }

    /// <summary>Fixed reader, mainly for tests.</summary>
    public GeolocationService(IGeoIP2DatabaseReader databaseReader, ILogger<GeolocationService> logger)
    {
        _database = new GeoIpDatabase();
        _database.Set(databaseReader);
        _logger = logger;
    }

    public string? GetCountryCode(HttpRequest request)
    {
        // 1. Cloudflare Header (Fastest and most accurate if behind Cloudflare)
        string? country = null;
        if (request.Headers.TryGetValue("CF-IPCountry", out var cfCountry))
            country = cfCountry.FirstOrDefault();

        // 2. Fallback headers (e.g. from other proxies or load balancers)
        if (string.IsNullOrWhiteSpace(country) && request.Headers.TryGetValue("X-Geoip-Country", out var xg))
            country = xg.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(country) && request.Headers.TryGetValue("X-Country", out var xc))
            country = xc.FirstOrDefault();

        // Validate extracted country code
        if (!string.IsNullOrWhiteSpace(country))
        {
            var code = country.Trim();
            // Cloudflare sends "XX" for unknown or "T1" for Tor. We usually want valid ISO codes.
            if (code.Length == 2 && !code.Equals("XX", StringComparison.OrdinalIgnoreCase) && !code.Equals("T1", StringComparison.OrdinalIgnoreCase))
            {
                return code.ToUpperInvariant();
            }
            // If "XX" or invalid, we fall through to IP lookup
        }
        _logger.LogWarning("Country code not found in headers");
        try
        {
            var ipAddress = GetRemoteIpAddress(request);
            if (ipAddress == null)
            {
                _logger.LogWarning("IP address not found");
                return null;
            }

            // Check if IP is loopback or private range if needed, but MaxMind handles this by throwing AddressNotFoundException usually or returning null
            // However, typical server environments might have local IPs.
            // Let's just try to query the DB.

            var reader = _database.Reader;
            if (reader is null)
            {
                _logger.LogDebug("GeoIP database not available; skipping IP lookup");
                return null;
            }

            if (reader.TryCountry(ipAddress, out var response) && response?.Country?.IsoCode != null)
            {
                return response.Country.IsoCode;
            }
        }
        catch (AddressNotFoundException)
        {
            // IP not found in database (e.g. private IP, localhost, or new IP)
            // This is expected for local development or internal traffic.
            _logger.LogWarning("IP address not found in database");
        }
        catch (GeoIP2Exception ex)
        {
            _logger.LogWarning(ex, "Error querying GeoIP database.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in GeolocationService.");
        }

        return null;
    }

    public string GetVisitorId(HttpRequest request)
    {
        var ip = GetRemoteIpAddress(request)?.ToString() ?? "unknown_ip";
        var ua = request.Headers.UserAgent.ToString();
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var raw = $"{ip}|{ua}|{date}|tempoo_salt";

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
        var hash = sha256.ComputeHash(bytes);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private System.Net.IPAddress? GetRemoteIpAddress(HttpRequest request)
    {
        // Check CF-Connecting-IP first (Cloudflare specific)
        if (request.Headers.TryGetValue("CF-Connecting-IP", out var cfIpHeader) && !string.IsNullOrWhiteSpace(cfIpHeader))
        {
            if (System.Net.IPAddress.TryParse(cfIpHeader.FirstOrDefault(), out var ip))
            {
                return ip;
            }
        }

        // Check X-Forwarded-For first (standard for proxies/load balancers)
        if (request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) && !string.IsNullOrWhiteSpace(forwardedFor))
        {
            // X-Forwarded-For can contain multiple IPs, the first one is the client
            var ipString = forwardedFor.FirstOrDefault()?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (System.Net.IPAddress.TryParse(ipString, out var ip))
            {
                return ip;
            }
        }

        // Fallback to connection remote IP
        return request.HttpContext.Connection.RemoteIpAddress;
    }
}
