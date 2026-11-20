namespace TempooAnalytics.Api.Infrastructure.Email;

public interface IEmailService
{
    /// <summary>False when no provider credentials are set (self-hosted without Resend): sends would be silently dropped.</summary>
    bool IsConfigured { get; }

    Task SendEmailAsync(string to, string subject, string htmlContent);

    /// <summary>Like <see cref="SendEmailAsync"/> but throws when the send fails or the provider is not configured, so a caller that records "sent" only does so when it was accepted. Optional extra headers.</summary>
    Task SendEmailOrThrowAsync(string to, string subject, string htmlContent, IReadOnlyDictionary<string, string>? headers = null);
    Task SendWelcomeEmailAsync(string to, string username);
    Task SendWelcomeWithCredentialsEmailAsync(string to, string username, string password);
    Task SendPasswordResetEmailAsync(string to, string username, string tempPassword);
    Task SendAccountAlreadyExistsEmailAsync(string to);
    Task SendMarketingEmailAsync(string to, string subject, string body);
    Task SendSubscriptionSuccessfulEmailAsync(string to, string planName);
    Task SendExportReadyEmailAsync(string to, string downloadUrl);
}
