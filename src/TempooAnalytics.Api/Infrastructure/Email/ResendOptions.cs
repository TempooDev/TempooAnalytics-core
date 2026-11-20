namespace TempooAnalytics.Api.Infrastructure.Email;

/// <summary>
/// Resend is optional. Without an API key and a sender address no email is sent and nothing is called on api.resend.com
/// (see <see cref="IEmailService.IsConfigured"/>). There is no default sender: it is whatever the operator sets in
/// <c>Resend:FromEmail</c> (for example <c>My Analytics &lt;no-reply@example.com&gt;</c>), on a domain verified in Resend.
/// </summary>
public class ResendOptions
{
    public const string SectionName = "Resend";

    public string ApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
}
