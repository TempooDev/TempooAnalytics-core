namespace TempooAnalytics.Api.Features.Heatmaps;

/// <summary>
/// Which browser origins may read a site's overlay data: the site's allowed origins or, when it has none,
/// the domain in its name (sites have no URL column; the name is usually the domain).
/// </summary>
public static class HeatmapOverlayOrigins
{
    public static bool IsAllowed(string? origin, IEnumerable<string>? allowedOrigins, string? siteName)
    {
        if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var originUri)) return false;
        if (originUri.Scheme is not ("https" or "http")) return false;

        var entries = (allowedOrigins ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        if (entries.Count == 0 && DomainFromName(siteName) is { } domain) entries.Add(domain);

        return entries.Any(entry => Matches(originUri, entry.Trim()));
    }

    /// <summary>Whether the data-site-id on the page (the site id or its key) names the site of the token.</summary>
    public static bool IsSameSite(string? pageSiteId, Guid siteId, string? siteKey)
    {
        var value = pageSiteId?.Trim();
        if (string.IsNullOrEmpty(value)) return false;
        return Guid.TryParse(value, out var guid)
            ? guid == siteId
            : string.Equals(value, siteKey, StringComparison.Ordinal);
    }

    /// <summary>Base URL of the customer's site, used by the app to open the overlay.</summary>
    public static string? SiteUrl(IEnumerable<string>? allowedOrigins, string? siteName)
    {
        var first = (allowedOrigins ?? []).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e))?.Trim() ?? DomainFromName(siteName);
        if (first is null) return null;
        if (!first.Contains("://")) first = "https://" + first;
        return Uri.TryCreate(first, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null;
    }

    // An entry with a scheme must match the origin exactly; a bare host also allows its subdomains.
    private static bool Matches(Uri origin, string entry)
    {
        if (entry.Contains("://"))
        {
            return Uri.TryCreate(entry, UriKind.Absolute, out var allowed)
                && string.Equals(allowed.GetLeftPart(UriPartial.Authority), origin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
        }

        var host = entry.Split('/', 2)[0].Split(':', 2)[0].TrimStart('.');
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        return origin.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            || origin.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);
    }

    private static string? DomainFromName(string? name)
    {
        var candidate = name?.Trim();
        if (string.IsNullOrEmpty(candidate) || candidate.Contains(' ') || !candidate.Contains('.')) return null;
        return Uri.CheckHostName(candidate.Split("://").Last().Split('/')[0]) == UriHostNameType.Dns ? candidate : null;
    }
}
