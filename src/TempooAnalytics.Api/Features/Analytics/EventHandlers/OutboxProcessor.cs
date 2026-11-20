using System.Text.Json;
using Dapper;
using Npgsql;

namespace TempooAnalytics.Api.Features.Analytics.EventHandlers;

public sealed class OutboxProcessor : BackgroundService
{
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly string _connectionString;
    private readonly TimeSpan _interval;
    private readonly IServiceProvider _serviceProvider;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public OutboxProcessor(IConfiguration configuration, ILogger<OutboxProcessor> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _connectionString = configuration.GetConnectionString("analytics-db")
                           ?? throw new InvalidOperationException("Missing connection string 'analytics-db'");

        var seconds = configuration.GetSection("Outbox")
                                   .GetValue("PollingIntervalSeconds", 10);
        _interval = TimeSpan.FromSeconds(seconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxProcessor started (interval: {Interval}s)", _interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessages(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox messages");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // ignore
            }
        }

        _logger.LogInformation("OutboxProcessor stopping");
    }

    private async Task ProcessPendingMessages(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = @"
SELECT id, type, payload
FROM outbox_messages
WHERE processed_at IS NULL
ORDER BY created_at
LIMIT 100;";

        var messages = await conn.QueryAsync<(Guid Id, string Type, string Payload)>(
            new CommandDefinition(sql, cancellationToken: ct));

        foreach (var msg in messages)
        {
            try
            {
                _logger.LogInformation("Processing outbox message {Id} type {Type}", msg.Id, msg.Type);

                using var scope = _serviceProvider.CreateScope();

                // Handle Unified Audit Logs (Admin & User actions)
                if (msg.Type == "audit.system_action" || msg.Type == "audit.admin_action")
                {
                    const string auditSql = @"
INSERT INTO audit_logs (id, actor_id, entity_id, entity_type, action, details, created_at)
VALUES (gen_random_uuid(), @ActorId, @EntityId, @EntityType, @Action, @Details::jsonb, @CreatedAt);";

                    var payload = JsonSerializer.Deserialize<AuditPayload>(msg.Payload, JsonOptions);
                    if (payload != null)
                    {
                        await conn.ExecuteAsync(new CommandDefinition(auditSql, new
                        {
                            payload.ActorId,
                            payload.EntityId,
                            payload.EntityType,
                            payload.Action,
                            Details = JsonSerializer.Serialize(payload.Details, JsonOptions),
                            payload.CreatedAt
                        }, cancellationToken: ct));
                    }
                }
                else if (msg.Type == "WELCOME_EMAIL")
                {
                    var payload = JsonSerializer.Deserialize<WelcomeEmailPayload>(msg.Payload, JsonOptions);
                    if (payload != null)
                    {
                        var emailSvc = scope.ServiceProvider.GetRequiredService<Infrastructure.Email.IEmailService>();
                        // This instance's web app (App:PublicUrl), never a fixed host.
                        var loginUrl = (scope.ServiceProvider.GetService<Infrastructure.Email.EmailLinks>() ?? Infrastructure.Email.EmailLinks.Default).App("/auth/login");
                        if (payload.Action == "WelcomeNewUser")
                        {
                            // Send an email with the activation link
                            var html = $@"
                                <div style='font-family: sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;'>
                                    <h1 style='color: #1e293b;'>Welcome to Tempoo Analytics!</h1>
                                    <p style='color: #475569; font-size: 16px;'>We're glad to have you on board. Your account has been created successfully.</p>
                                    
                                    <div style='background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 20px; margin: 24px 0;'>
                                        <p style='margin: 0 0 12px 0; color: #64748b; font-size: 14px; text-transform: uppercase; letter-spacing: 0.05em; font-weight: bold;'>Login Credentials</p>
                                        <p style='margin: 0 0 8px 0; color: #1e293b;'><strong>Username:</strong> {payload.Email}</p>
                                        <p style='margin: 0; color: #1e293b;'><em>Use the button below to set your password and access your account.</em></p>
                                    </div>

                                    <p style='color: #475569; font-size: 16px;'>To get started, please set your password by clicking the button below:</p>
                                    <div style='text-align: center; margin: 30px 0;'>
                                        <a href='{payload.ActivationUrl}' style='background-color: #0284c7; color: white; padding: 14px 28px; text-decoration: none; border-radius: 8px; font-weight: bold; display: inline-block;'>Set My Password</a>
                                    </div>
                                    <p style='color: #64748b; font-size: 14px;'>This link will expire in 48 hours.</p>
                                    <p style='color: #64748b; font-size: 14px; margin-top: 20px;'>After setting your password, you can always log in at: <a href='{loginUrl}'>{loginUrl}</a></p>
                                </div>";
                            await emailSvc.SendEmailAsync(payload.Email, "Welcome to Tempoo Analytics!", html);
                        }
                        else if (payload.Action == "SubscriptionUpdated")
                        {
                            await emailSvc.SendSubscriptionSuccessfulEmailAsync(payload.Email, payload.Tier);
                        }
                    }
                }

                const string updateSql = @"
UPDATE outbox_messages
SET processed_at = NOW(), error = NULL
WHERE id = @id;";

                await conn.ExecuteAsync(
                    new CommandDefinition(updateSql, new { id = msg.Id }, cancellationToken: ct));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox message {Id}", msg.Id);

                const string updateErrorSql = @"
UPDATE outbox_messages
SET error = @error
WHERE id = @id;";

                await conn.ExecuteAsync(
                    new CommandDefinition(updateErrorSql, new { id = msg.Id, error = ex.Message }, cancellationToken: ct));
            }
        }
    }

    private record AuditPayload(
        string ActorId,
        string EntityId,
        string EntityType,
        string Action,
        object Details,
        DateTime CreatedAt
    );

    private record WelcomeEmailPayload(
        string Email,
        Guid UserId,
        string Tier,
        string Action,
        string ActivationUrl
    );
}
