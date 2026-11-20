using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Dtos;

namespace TempooAnalytics.Api.Infrastructure.Services;

/// <summary>An alert for the owner of Tempoo (sent to the bot), not for users: see <c>Features.Notifications</c> for those.</summary>
public sealed record AdminAlert(string Source, string Message);

/// <summary>Sends alerts to the owner's bot. Fire and forget: a failed alert is only logged.</summary>
public interface IAdminAlertService : IService
{
    Task AlertAsync(AdminAlert alert);
}

/// <summary>
/// Where the owner's alerts go (<c>AdminAlerts:BotUrl</c>). Empty by default: a self-hosted instance sends nothing anywhere, in
/// particular the text of its users' feedback never leaves it. The hosted service sets its own destination.
/// </summary>
public class AdminAlertOptions
{
    public const string SectionName = "AdminAlerts";

    public string BotUrl { get; set; } = string.Empty;
}

public class AdminAlertService(IHttpClientFactory httpClientFactory, IOptions<AdminAlertOptions> options, ILogger<AdminAlertService> logger) : IAdminAlertService
{
    public Task AlertAsync(AdminAlert alert)
    {
        var botUrl = options.Value.BotUrl;
        if (string.IsNullOrWhiteSpace(botUrl))
        {
            logger.LogDebug("No AdminAlerts:BotUrl configured: alert from {Source} not sent", alert.Source);
            return Task.CompletedTask;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var client = httpClientFactory.CreateClient();
                var response = await client.PostAsJsonAsync(botUrl, new AdminAlertRequest(alert.Source, alert.Message), AppJsonContext.Default.AdminAlertRequest);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Failed to send admin alert from {Source}. Status: {StatusCode}", alert.Source, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error sending admin alert from {Source}", alert.Source);
            }
        });

        return Task.CompletedTask;
    }
}

/// <summary>The alerts sent to the owner, one method per event.</summary>
public static class AdminAlerts
{
    public static AdminAlert LeadRegistered(string email, string? sourceUrl) => new(
        string.IsNullOrWhiteSpace(sourceUrl) ? "Landing" : sourceUrl,
        $"New lead registered: {MaskEmail(email)}");

    public static AdminAlert FeedbackSubmitted(string username, string title, string description) => new(
        "App Feedback",
        $"New Feedback from {username}\nTitle: {title}\nDescription: {description}");

    /// <summary>
    /// The weekly digest couldn't write some summaries because of tokens: the answer hit the output limit, or the
    /// provider is out of quota or credit. Those summaries are retried and, after three failures, sent with numbers only.
    /// </summary>
    public static AdminAlert WeeklyDigestTokens(DateOnly weekStart, int truncated, int quota, string? model, string? lastError) => new(
        "AI weekly digest",
        string.Join("\n", new[]
        {
            $"Weekly digest {weekStart:yyyy-MM-dd}: model {model ?? "?"} ran out of tokens.",
            truncated > 0 ? $"- {truncated} answer(s) cut off at the output limit: raise Ai:WeeklyDigest:MaxOutputTokens." : null,
            quota > 0 ? $"- {quota} call(s) refused for quota or credit: check the provider account." : null,
            string.IsNullOrWhiteSpace(lastError) ? null : $"Last error: {(lastError.Length > 300 ? lastError[..300] : lastError)}",
            "Affected summaries are retried; after 3 failures they go out with numbers only."
        }.Where(l => l != null)));

    /// <summary>Keeps the first 3 characters of the local part: "ant****@example.com".</summary>
    public static string MaskEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;
        var parts = email.Split('@');
        if (parts.Length != 2) return email;

        var local = parts[0];
        var domain = parts[1];

        if (local.Length <= 3)
        {
            return $"{local}@{domain}";
        }

        var visible = local.Substring(0, 3);
        var masked = new string('*', local.Length - 3);
        return $"{visible}{masked}@{domain}";
    }
}
