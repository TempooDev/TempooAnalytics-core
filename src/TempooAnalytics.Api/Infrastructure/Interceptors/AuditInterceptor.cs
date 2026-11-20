using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Infrastructure.Interceptors;

public class AuditInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IReadOnlyList<IAuditRule> _rules;

    /// <param name="httpContextAccessor">Where the actor comes from.</param>
    /// <param name="moduleRules">The <see cref="IAuditRule"/>s modules registered; none in a host without modules.</param>
    public AuditInterceptor(IHttpContextAccessor httpContextAccessor, IEnumerable<IAuditRule>? moduleRules = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _rules = [.. CoreAuditRules.All, .. moduleRules ?? []];
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context == null) return base.SavingChangesAsync(eventData, result, cancellationToken);

        var actorId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? _httpContextAccessor.HttpContext?.User?.FindFirstValue("sub")
                    ?? "system";

        var outboxMessages = new List<OutboxMessage>();

        // The core's rules first (sites, users), then the ones modules registered, in registration order.
        foreach (var rule in _rules)
        {
            AuditEntityChanges(context, actorId, outboxMessages, rule);
        }

        if (outboxMessages.Count > 0)
        {
            context.Set<OutboxMessage>().AddRange(outboxMessages);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AuditEntityChanges(DbContext context, string actorId, List<OutboxMessage> outboxMessages, IAuditRule rule)
    {
        var entries = context.ChangeTracker.Entries().Where(e => rule.EntityType.IsInstanceOfType(e.Entity)).ToList();
        foreach (var entry in entries)
        {
            if (rule.Describe(entry) is not { } audit) continue;

            var entityId = GetEntityId(entry);
            var auditPayload = new
            {
                ActorId = actorId,
                EntityId = entityId,
                EntityType = audit.EntityType,
                Action = audit.Action,
                Details = audit.Details,
                CreatedAt = DateTime.UtcNow
            };

            outboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = "audit.system_action",
                Payload = JsonDocument.Parse(JsonSerializer.Serialize(auditPayload, JsonOptions)),
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    private string GetEntityId(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key == null) return "unknown";

        var values = key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? "null");
        return string.Join(":", values);
    }
}
