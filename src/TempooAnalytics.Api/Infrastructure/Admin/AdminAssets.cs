using Microsoft.AspNetCore.Html;

namespace TempooAnalytics.Api.Infrastructure.Admin;

/// <summary>
/// The stylesheets of the admin console, inlined in each page: the pages load nothing from a third-party CDN (Tailwind,
/// PrimeIcons and Google Fonts used to come from cdn.tailwindcss.com, unpkg.com and fonts.googleapis.com). They are embedded
/// in this assembly, so any host that serves the pages (the core API or the cloud composition) has them without static files.
/// Regenerate with <c>scripts/admin-assets/build.sh</c>.
/// </summary>
public static class AdminAssets
{
    private static readonly Lazy<HtmlString> _styles = new(() => Style("admin.css"));
    private static readonly Lazy<HtmlString> _font = new(() => Style("admin-font.css"));

    /// <summary>Tailwind utilities and the PrimeIcons glyphs the admin pages use, as a <c>&lt;style&gt;</c> element.</summary>
    public static HtmlString Styles => _styles.Value;

    /// <summary>The Plus Jakarta Sans font face, as a <c>&lt;style&gt;</c> element.</summary>
    public static HtmlString Font => _font.Value;

    internal static string Read(string name)
    {
        var resource = $"TempooAnalytics.Api.AdminAssets.{name}";
        using var stream = typeof(AdminAssets).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource {resource} not found");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static HtmlString Style(string name) => new($"<style>\n{Read(name)}</style>");
}
