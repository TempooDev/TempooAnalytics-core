using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Auth;

namespace TempooAnalytics.Api.Infrastructure.Interceptors;

/// <summary>The core's audit rules (sites and users). Applied by <see cref="AuditInterceptor"/> before the rules modules register.</summary>
public static class CoreAuditRules
{
    public static IReadOnlyList<IAuditRule> All { get; } = [new AnalyticsSiteAuditRule(), new UserAuditRule()];
}

internal sealed class AnalyticsSiteAuditRule : AuditRule<AnalyticsSite>
{
    protected override AuditDescription? Describe(EntityEntry<AnalyticsSite> entry)
    {
        var details = new Dictionary<string, object?>();

        if (entry.State == EntityState.Added)
        {
            details["site_key"] = entry.Entity.SiteKey;
            details["owner_id"] = entry.Entity.OwnerId?.ToString();
            return new("AnalyticsSite", "Created", details);
        }

        var writeKeyProp = entry.Property(x => x.WriteKey);
        if (writeKeyProp.IsModified)
        {
            details["action"] = writeKeyProp.CurrentValue == null ? "Revoked" : "Rotated";
            return new("AnalyticsSite", "WriteKeyChanged", details);
        }

        var isDeletedProp = entry.Property(x => x.IsDeleted);
        if (isDeletedProp.IsModified && (bool)isDeletedProp.CurrentValue!)
        {
            return new("AnalyticsSite", "Deleted", null);
        }

        return null;
    }
}

internal sealed class UserAuditRule : AuditRule<User>
{
    protected override AuditDescription? Describe(EntityEntry<User> entry)
    {
        var details = new Dictionary<string, object?>();

        if (entry.State == EntityState.Added)
        {
            details["username"] = entry.Entity.Username;
            return new("User", "Created", details);
        }

        // Detect sensitive changes
        var passwordProp = entry.Property(x => x.PasswordHash);
        if (passwordProp.IsModified)
        {
            return new("User", "PasswordChanged", null);
        }

        var isAdminProp = entry.Property(x => x.IsAdmin);
        if (isAdminProp.IsModified)
        {
            details["is_admin"] = isAdminProp.CurrentValue;
            return new("User", "RoleChanged", details);
        }

        var isActiveProp = entry.Property(x => x.IsActive);
        if (isActiveProp.IsModified)
        {
            details["is_active"] = isActiveProp.CurrentValue;
            return new("User", "StatusChanged", details);
        }

        var emailProp = entry.Property(x => x.Email);
        if (emailProp.IsModified)
        {
            details["old_email"] = emailProp.OriginalValue;
            details["new_email"] = emailProp.CurrentValue;
            return new("User", "EmailChanged", details);
        }

        return null;
    }
}
