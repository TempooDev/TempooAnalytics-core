using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace TempooAnalytics.Api.Infrastructure.Interceptors;

/// <summary>What an <see cref="IAuditRule"/> says about one change: the entity type name and action recorded in the audit message, and its details.</summary>
public readonly record struct AuditDescription(string EntityType, string Action, object? Details);

/// <summary>
/// How the <see cref="AuditInterceptor"/> audits one entity type. The core contributes its own rules; modules add theirs by
/// registering an <see cref="IAuditRule"/> in <c>ConfigureServices</c>, so the core never names a module's entities.
/// Rules are applied in registration order (core rules first), each over the tracked entries of its <see cref="EntityType"/>.
/// </summary>
public interface IAuditRule
{
    /// <summary>The CLR type of the entities this rule audits (entries are matched as <c>Entries&lt;T&gt;</c> does: the type and its subtypes).</summary>
    Type EntityType { get; }

    /// <summary>The audit record for this change, or null when the change is not audited.</summary>
    AuditDescription? Describe(EntityEntry entry);
}

/// <summary>Typed convenience over <see cref="IAuditRule"/>.</summary>
public abstract class AuditRule<T> : IAuditRule where T : class
{
    public Type EntityType => typeof(T);

    public AuditDescription? Describe(EntityEntry entry) => Describe(entry.Context.Entry((T)entry.Entity));

    protected abstract AuditDescription? Describe(EntityEntry<T> entry);
}
