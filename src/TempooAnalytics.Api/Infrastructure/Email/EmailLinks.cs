using Microsoft.Extensions.Options;

namespace TempooAnalytics.Api.Infrastructure.Email;

/// <summary>
/// Where the links and images of transactional emails point (section <c>App</c>). Every value is optional: a self-hosted
/// instance sets <c>App:PublicUrl</c> to the address its users open (the gateway), and the rest is derived from it. Nothing
/// here names a hosted service; the hosted service sets its own values from its module.
/// </summary>
public sealed class PublicUrlOptions
{
    public const string SectionName = "App";

    /// <summary>Base URL of the web app, e.g. <c>https://analytics.example.com</c>. Empty = the first <c>Cors:AllowedOrigins</c>
    /// entry, or <c>http://localhost:8000</c> (the self-host gateway's default port).</summary>
    public string PublicUrl { get; set; } = "";

    /// <summary>Base URL of a separate documentation/marketing site. Empty = the app's own <c>/docs</c> page.</summary>
    public string SiteUrl { get; set; } = "";

    /// <summary>Absolute URL of the logo shown at the top of emails. Empty = the logo served by the instance itself.</summary>
    public string EmailLogoUrl { get; set; } = "";
}

/// <summary>Builds the absolute URLs used in emails from <see cref="PublicUrlOptions"/>, resolved once on first use.</summary>
public sealed class EmailLinks
{
    public const string DefaultPublicUrl = "http://localhost:8000";

    private readonly Lazy<Resolved> _resolved;

    public EmailLinks(IOptions<PublicUrlOptions> options, IConfiguration? configuration = null)
    {
        _resolved = new Lazy<Resolved>(() => Resolve(options.Value, configuration));
    }

    /// <summary>Links built only from the core defaults (no configuration).</summary>
    public static EmailLinks Default { get; } = new(Options.Create(new PublicUrlOptions()));

    /// <summary>Base URL of the web app, without a trailing slash.</summary>
    public string PublicUrl => _resolved.Value.PublicUrl;

    /// <summary>Absolute URL of the email logo.</summary>
    public string LogoUrl => _resolved.Value.LogoUrl;

    /// <summary>Host name shown in text such as "signed up on {host}".</summary>
    public string SiteName => _resolved.Value.SiteName;

    /// <summary>True when a separate documentation site is configured (<c>App:SiteUrl</c>).</summary>
    public bool HasSite => _resolved.Value.SiteUrl.Length > 0;

    /// <summary>Absolute URL of a path of the web app: <c>App("/auth/login")</c>.</summary>
    public string App(string path) => PublicUrl + path;

    /// <summary>Absolute URL of a path of the documentation site, or the app's <c>/docs</c> page when there is none.</summary>
    public string Site(string path) => HasSite ? _resolved.Value.SiteUrl + path : App("/docs");

    private sealed record Resolved(string PublicUrl, string SiteUrl, string LogoUrl, string SiteName);

    private static Resolved Resolve(PublicUrlOptions o, IConfiguration? configuration)
    {
        var publicUrl = Trim(o.PublicUrl);
        if (publicUrl.Length == 0)
        {
            var origins = configuration?.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            publicUrl = Trim(origins.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "");
        }
        if (publicUrl.Length == 0) publicUrl = DefaultPublicUrl;

        var siteUrl = Trim(o.SiteUrl);
        var logo = string.IsNullOrWhiteSpace(o.EmailLogoUrl)
            ? publicUrl + "/assets/images/tempoo-analytics-optimized.svg"
            : o.EmailLogoUrl.Trim();
        var siteName = HostOf(siteUrl.Length > 0 ? siteUrl : publicUrl);
        return new Resolved(publicUrl, siteUrl, logo, siteName);
    }

    private static string Trim(string? url) => (url ?? "").Trim().TrimEnd('/');

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Authority : url;
}
