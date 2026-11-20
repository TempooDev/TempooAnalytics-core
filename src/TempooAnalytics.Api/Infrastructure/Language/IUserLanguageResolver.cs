// Kept in the Onboarding.Services namespace (its consumers are there); the file lives outside Features/Onboarding because that folder is private.
namespace TempooAnalytics.Api.Features.Onboarding.Services;

/// <summary>Language code of the emails sent to a user (Spanish or English).</summary>
public interface IUserLanguageResolver
{
    Task<string> GetLanguageAsync(string email, CancellationToken ct = default);
}

/// <summary>
/// What the core runs with when no module says otherwise: always "es", the same fallback the lead-aware resolver
/// uses when it finds no lead. The cloud's leads module replaces it with the one that reads the sign-up language.
/// </summary>
public sealed class DefaultUserLanguageResolver : IUserLanguageResolver
{
    public const string FallbackLanguage = "es";

    public Task<string> GetLanguageAsync(string email, CancellationToken ct = default) => Task.FromResult(FallbackLanguage);
}
