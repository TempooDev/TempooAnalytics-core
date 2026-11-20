using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TempooAnalytics.Api.Infrastructure.Email;

public class ResendEmailService : IEmailService
{
    private readonly HttpClient _httpClient;
    private readonly ResendOptions _options;
    private readonly ILogger<ResendEmailService> _logger;
    private readonly EmailLinks _links;

    public ResendEmailService(
        HttpClient httpClient, 
        IOptions<ResendOptions> options, 
        ILogger<ResendEmailService> logger,
        EmailLinks? links = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        // App:PublicUrl and friends (EmailLinks): where the links and the logo of every email point.
        _links = links ?? EmailLinks.Default;
        
        // This class is built for every scope that sends mail: say once, not on every construction, that the provider is off.
        if (!IsConfigured && Interlocked.Exchange(ref _notConfiguredLogged, 1) == 0)
        {
            _logger.LogInformation(
                "Email is disabled: Resend is not configured (set Resend:ApiKey and Resend:FromEmail to enable it). Nothing is sent to api.resend.com.");
        }

        _httpClient.BaseAddress = new Uri("https://api.resend.com/");
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    private static int _notConfiguredLogged;

    /// <summary>True when both the API key and the sender address are set; otherwise every send is skipped without a network call.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey) && !string.IsNullOrWhiteSpace(_options.FromEmail);

    public async Task SendEmailAsync(string to, string subject, string htmlContent)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("Email provider not configured: not sending \"{Subject}\"", subject);
            return;
        }

        try
        {
            await PostEmailAsync(to, subject, htmlContent, null);
        }
        catch (HttpRequestException ex) when (ex.StatusCode != null)
        {
            // Already logged with Resend's response.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while sending email to {To}", to);
        }
    }

    /// <summary>Sends through Resend and throws when it fails (or the provider is not configured), so callers that record the send can tell.</summary>
    public Task SendEmailOrThrowAsync(string to, string subject, string htmlContent, IReadOnlyDictionary<string, string>? headers = null) =>
        PostEmailAsync(to, subject, htmlContent, headers?.ToDictionary(h => h.Key, h => h.Value));

    private async Task PostEmailAsync(string to, string subject, string htmlContent, Dictionary<string, string>? headers)
    {
        if (!IsConfigured) throw new InvalidOperationException("Email provider not configured (Resend:ApiKey and Resend:FromEmail).");

        object payload = headers == null
            ? new { from = _options.FromEmail, to = new[] { to }, subject, html = htmlContent }
            : new { from = _options.FromEmail, to = new[] { to }, subject, html = htmlContent, headers };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("emails", content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to send email via Resend. Status: {StatusCode}, Error: {Error}", response.StatusCode, error);
            throw new HttpRequestException($"Resend returned {(int)response.StatusCode}", null, response.StatusCode);
        }
    }

    public async Task SendWelcomeEmailAsync(string to, string username)
    {
        var subject = "Welcome to Tempoo Analytics! 🚀";
        var body = $@"
            <div style=""background-color: #f1f5f9; padding: 40px 20px; font-family: sans-serif;"">
                <div style=""max-width: 600px; margin: 0 auto; background-color: #ffffff; padding: 40px; border-radius: 12px; border-top: 4px solid #0284c7; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);"">
                    <div style=""text-align: center; margin-bottom: 32px;"">
                        <img src=""{_links.LogoUrl}"" alt=""Tempoo Analytics"" style=""height: 48px; width: auto; max-width: 100%; display: block; margin: 0 auto;"">
                    </div>
                    <h1 style=""color: #1e293b; font-size: 24px; text-align: center; margin-top: 0;"">Welcome, {username}!</h1>
                    <p style=""color: #475569; line-height: 1.6;"">We're thrilled to have you on board. Tempoo Analytics is designed to give you the most accurate and real-time insights into your web traffic.</p>
                    <p style=""color: #475569; line-height: 1.6;"">To get started, follow these simple steps:</p>
                    <ol style=""color: #475569; line-height: 1.6;"">
                        <li>Login to your dashboard.</li>
                        <li>Create your first Site.</li>
                        <li>Copy the integration script and paste it into your website's <code>&lt;head&gt;</code>.</li>
                    </ol>
                    <div style=""text-align: center; margin: 32px 0;"">
                        <a href=""{_links.App("/login")}"" style=""background-color: #0284c7; color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: bold; display: inline-block;"">Get Started</a>
                    </div>
                    <p style=""margin-top: 30px; color: #64748b; font-size: 14px; text-align: center;"">If you have any questions, feel free to reply to this email.</p>
                    <div style=""margin-top: 40px; padding-top: 20px; border-top: 1px solid #e2e8f0; font-size: 12px; color: #94a3b8; text-align: center;"">
                        © {DateTime.UtcNow.Year} Tempoo Analytics. All rights reserved.
                    </div>
                </div>
            </div>";

        await SendEmailAsync(to, subject, body);
    }

    public async Task SendWelcomeWithCredentialsEmailAsync(string to, string username, string password)
    {
        var subject = "Your Tempoo Analytics Account is Ready! 🚀";
        var body = $@"
            <div style=""background-color: #f1f5f9; padding: 40px 20px; font-family: sans-serif;"">
                <div style=""max-width: 600px; margin: 0 auto; background-color: #ffffff; padding: 40px; border-radius: 12px; border-top: 4px solid #0284c7; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);"">
                    <div style=""text-align: center; margin-bottom: 32px;"">
                        <img src=""{_links.LogoUrl}"" alt=""Tempoo Analytics"" style=""height: 48px; width: auto; max-width: 100%; display: block; margin: 0 auto;"">
                    </div>
                    <h1 style=""color: #1e293b; font-size: 24px; text-align: center; margin-top: 0;"">Welcome to Tempoo Analytics!</h1>
                    <p style=""color: #475569; line-height: 1.6;"">Hello,</p>
                    <p style=""color: #475569; line-height: 1.6;"">Since you are part of the Tempoo Analytics family, we have automatically created an account for you to start using the platform.</p>
                    
                    <div style=""background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 20px; margin: 24px 0;"">
                        <p style=""margin: 0 0 12px 0; color: #64748b; font-size: 14px; text-transform: uppercase; letter-spacing: 0.05em; font-weight: bold;"">Your Access Credentials</p>
                        <p style=""margin: 0 0 8px 0; color: #1e293b;""><strong>Username:</strong> {username}</p>
                        <p style=""margin: 0; color: #1e293b;""><strong>Password:</strong> <code style=""background: #e2e8f0; padding: 2px 6px; border-radius: 4px; font-size: 16px; color: #0284c7;"">{password}</code></p>
                    </div>

                    <div style=""text-align: center; margin: 32px 0;"">
                        <a href=""{_links.App("/auth/login")}"" style=""background-color: #0284c7; color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: bold; display: inline-block;"">Login to Dashboard</a>
                    </div>

                    <div style=""border-top: 1px solid #e2e8f0; padding-top: 24px; margin-top: 24px;"">
                        <h3 style=""color: #1e293b; font-size: 16px; margin-top: 0;"">Quick Start Guide:</h3>
                        <ol style=""color: #475569; line-height: 1.6; padding-left: 20px;"">
                            <li><strong>Login</strong> with the credentials provided above.</li>
                            <li><strong>Create your first Site</strong> in the dashboard.</li>
                            <li><strong>Copy the integration script</strong> and paste it into your website's <code>&lt;head&gt;</code>.</li>
                        </ol>
                    </div>

                    <p style=""font-size: 13px; color: #64748b; text-align: center; margin-top: 32px;"">Please change your password after your first login for security reasons.</p>
                    
                    <div style=""margin-top: 40px; padding-top: 20px; border-top: 1px solid #e2e8f0; font-size: 12px; color: #94a3b8; text-align: center;"">
                        © {DateTime.UtcNow.Year} Tempoo Analytics. All rights reserved.
                    </div>
                </div>
            </div>";

        await SendEmailAsync(to, subject, body);
    }

    public async Task SendPasswordResetEmailAsync(string to, string username, string tempPassword)
    {
        var subject = "Your Password has been Reset - Tempoo Analytics 🔒";
        var body = $@"
            <div style=""background-color: #f1f5f9; padding: 40px 20px; font-family: sans-serif;"">
                <div style=""max-width: 600px; margin: 0 auto; background-color: #ffffff; padding: 40px; border-radius: 12px; border-top: 4px solid #0284c7; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);"">
                    <div style=""text-align: center; margin-bottom: 32px;"">
                        <img src=""{_links.LogoUrl}"" alt=""Tempoo Analytics"" style=""height: 48px; width: auto; max-width: 100%; display: block; margin: 0 auto;"">
                    </div>
                    <h1 style=""color: #1e293b; font-size: 24px; text-align: center; margin-top: 0;"">Password Reset</h1>
                    <p style=""color: #475569; line-height: 1.6;"">Hello <strong>{username}</strong>,</p>
                    <p style=""color: #475569; line-height: 1.6;"">As requested, your password has been reset. You can now log in using the temporary credentials below.</p>
                    
                    <div style=""background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 20px; margin: 24px 0;"">
                        <p style=""margin: 0 0 12px 0; color: #64748b; font-size: 14px; text-transform: uppercase; letter-spacing: 0.05em; font-weight: bold;"">New Temporary Password</p>
                        <p style=""margin: 0; color: #1e293b;""><strong>Password:</strong> <code style=""background: #e2e8f0; padding: 2px 6px; border-radius: 4px; font-size: 16px; color: #0284c7;"">{tempPassword}</code></p>
                    </div>

                    <div style=""text-align: center; margin: 32px 0;"">
                        <a href=""{_links.App("/login")}"" style=""background-color: #0284c7; color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: bold; display: inline-block;"">Login to Dashboard</a>
                    </div>

                    <p style=""font-size: 14px; color: #64748b; text-align: center;"">For your security, please change this password immediately after logging in.</p>
                    
                    <div style=""margin-top: 40px; padding-top: 20px; border-top: 1px solid #e2e8f0; font-size: 12px; color: #94a3b8; text-align: center;"">
                        © {DateTime.UtcNow.Year} Tempoo Analytics. All rights reserved.
                    </div>
                </div>
            </div>";

        await SendEmailAsync(to, subject, body);
    }

    public async Task SendAccountAlreadyExistsEmailAsync(string to)
    {
        var subject = "You already have a Tempoo Analytics account";
        var body = $@"
            <div style=""background-color: #f1f5f9; padding: 40px 20px; font-family: sans-serif;"">
                <div style=""max-width: 600px; margin: 0 auto; background-color: #ffffff; padding: 40px; border-radius: 12px; border-top: 4px solid #0284c7; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);"">
                    <div style=""text-align: center; margin-bottom: 32px;"">
                        <img src=""{_links.LogoUrl}"" alt=""Tempoo Analytics"" style=""height: 48px; width: auto; max-width: 100%; display: block; margin: 0 auto;"">
                    </div>
                    <h1 style=""color: #1e293b; font-size: 24px; text-align: center; margin-top: 0;"">You already have an account</h1>
                    <p style=""color: #475569; line-height: 1.6;"">Hello,</p>
                    <p style=""color: #475569; line-height: 1.6;"">Someone (hopefully you) just signed up on {_links.SiteName} with this email address. There is already a Tempoo Analytics account for it, so we haven't changed anything: your password is still the same.</p>

                    <div style=""text-align: center; margin: 32px 0;"">
                        <a href=""{_links.App("/auth/login")}"" style=""background-color: #0284c7; color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: bold; display: inline-block;"">Log in to Dashboard</a>
                    </div>

                    <p style=""color: #475569; line-height: 1.6; text-align: center;"">Forgot your password? <a href=""{_links.App("/auth/forgot")}"" style=""color: #0284c7;"">Reset it here</a>.</p>
                    <p style=""font-size: 13px; color: #64748b; text-align: center; margin-top: 32px;"">If this wasn't you, you can safely ignore this email.</p>

                    <div style=""margin-top: 40px; padding-top: 20px; border-top: 1px solid #e2e8f0; font-size: 12px; color: #94a3b8; text-align: center;"">
                        © {DateTime.UtcNow.Year} Tempoo Analytics. All rights reserved.
                    </div>
                </div>
            </div>";

        await SendEmailAsync(to, subject, body);
    }

    public async Task SendMarketingEmailAsync(string to, string subject, string bodyContent)
    {
        var body = $@"
            <div style=""background-color: #f1f5f9; padding: 40px 20px; font-family: sans-serif;"">
                <div style=""max-width: 600px; margin: 0 auto; background-color: #ffffff; padding: 40px; border-radius: 12px; border-top: 4px solid #0284c7; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);"">
                    <div style=""text-align: center; margin-bottom: 32px;"">
                        <img src=""{_links.LogoUrl}"" alt=""Tempoo Analytics"" style=""height: 48px; width: auto; max-width: 100%; display: block; margin: 0 auto;"">
                    </div>
                    <div style=""color: #475569; line-height: 1.6;"">
                        {bodyContent}
                    </div>
                    <div style=""margin-top: 40px; padding-top: 20px; border-top: 1px solid #e2e8f0; font-size: 12px; color: #94a3b8; text-align: center;"">
                        You're receiving this because you signed up for Tempoo Analytics.<br>
                        © {DateTime.UtcNow.Year} Tempoo Analytics. All rights reserved.
                    </div>
                </div>
            </div>";

        await SendEmailAsync(to, subject, body);
    }

    public async Task SendSubscriptionSuccessfulEmailAsync(string to, string planName)
    {
        var subject = $"Welcome to Tempoo {planName}!";
        var body = $@"
        <div style=""font-family: sans-serif; padding: 20px; color: #1e293b;"">
            <div style=""text-align: center; margin-bottom: 30px;"">
                <img src=""https://tempoo.analytics/logo.png"" alt=""Tempoo Logo"" style=""width: 150px;"" />
            </div>
            <h2 style=""color: #0f172a; text-align: center;"">Your subscription is active!</h2>
            <p style=""font-size: 16px; line-height: 1.6;"">Hello,</p>
            <p style=""font-size: 16px; line-height: 1.6;"">Thanks for upgrading to the <strong>{planName}</strong> plan. Your limits have been updated automatically across all your sites.</p>

            <div style=""background-color: #f8fafc; border-radius: 8px; padding: 20px; margin: 30px 0;"">
                <h3 style=""margin-top: 0; font-size: 14px; text-transform: uppercase; color: #64748b;"">What's next?</h3>
                <ul style=""padding-left: 20px; margin-bottom: 0;"">
                    <li style=""margin-bottom: 10px;"">Explore your new advanced metrics.</li>
                    <li style=""margin-bottom: 10px;"">Set up custom events if you haven't already.</li>
                    <li>Check your monthly usage in the Billing section.</li>
                </ul>
            </div>

            <div style=""text-align: center; margin-top: 40px;"">
                <a href=""https://app.tempoo.analytics/dashboard"" style=""background-color: #0f172a; color: #ffffff; padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: 600;"">Go to Dashboard</a>
            </div>

            <p style=""margin-top: 40px; color: #64748b; font-size: 14px; text-align: center;"">If you have any questions, feel free to reply to this email.</p>
        </div>";

        await SendEmailAsync(to, subject, body);
    }

    public async Task SendExportReadyEmailAsync(string to, string downloadUrl)
    {
        var subject = "Your Data Export is Ready! 📊";
        var body = $@"
            <div style=""background-color: #f1f5f9; padding: 40px 20px; font-family: sans-serif;"">
                <div style=""max-width: 600px; margin: 0 auto; background-color: #ffffff; padding: 40px; border-radius: 12px; border-top: 4px solid #fbbf24; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);"">
                    <div style=""text-align: center; margin-bottom: 32px;"">
                        <img src=""https://tempoo.analytics/logo.png"" alt=""Tempoo Analytics"" style=""height: 48px; width: auto; max-width: 100%; display: block; margin: 0 auto;"">
                    </div>
                    <h1 style=""color: #1e293b; font-size: 22px; text-align: center;"">Your Export is Ready</h1>
                    <p style=""color: #475569; line-height: 1.6;"">The data export you requested has been generated successfully. You can download it using the button below:</p>
                    <div style=""text-align: center; margin: 32px 0;"">
                        <a href=""{downloadUrl}"" style=""background-color: #fbbf24; color: #1e293b; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: bold; display: inline-block;"">Download CSV Export</a>
                    </div>
                    <p style=""color: #64748b; font-size: 13px; text-align: center;"">⚠️ This link will expire in 24 hours for security reasons.</p>
                    <div style=""margin-top: 40px; padding-top: 20px; border-top: 1px solid #e2e8f0; font-size: 12px; color: #94a3b8; text-align: center;"">
                        © {DateTime.UtcNow.Year} Tempoo Analytics. All rights reserved.
                    </div>
                </div>
            </div>";

        await SendEmailAsync(to, subject, body);
    }
}
