namespace TempooAnalytics.Api.Features.Imports.Ga4;

/// <summary>
/// Google OAuth client used to import from Google Analytics 4 ("Google" configuration section).
/// RedirectUri is the full URL of the app's callback route (/account/imports/google/callback).
/// </summary>
public sealed class GoogleOAuthOptions
{
    public const string SectionName = "Google";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RedirectUri { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret) &&
        Uri.TryCreate(RedirectUri, UriKind.Absolute, out _);
}
