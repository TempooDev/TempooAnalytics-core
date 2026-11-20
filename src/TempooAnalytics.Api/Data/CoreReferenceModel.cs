using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Data;

/// <summary>
/// The core tables that module tables point at (users and sites). Both <see cref="AnalyticsDbContext"/> and
/// the module context of the cloud edition map them the same way; only the core context migrates them.
/// The user table is configured after the module tables, as it always was: foreign key names depend on that order
/// (they point at "users"), so moving it changes the model.
/// </summary>
public static class CoreReferenceModel
{
    public static ModelBuilder ConfigureCoreReferenceConverters(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().Property(e => e.Id).HasConversion(id => id.Value, v => new UserId(v));
        modelBuilder.Entity<AnalyticsSite>().Property(e => e.Id).HasConversion(id => id.Value, v => new SiteId(v));
        modelBuilder.Entity<AnalyticsSite>().Property(e => e.OwnerId).HasConversion(id => id.HasValue ? id.Value.Value : (Guid?)null, v => v.HasValue ? new UserId(v.Value) : null);

        return modelBuilder;
    }

    /// <summary>
    /// The outbox, which <c>AuditInterceptor</c> writes audit messages to in the same SaveChanges as the audited change.
    /// The private context maps it as a reference so its own changes (subscriptions, tiers) are audited atomically too.
    /// </summary>
    public static ModelBuilder ConfigureOutboxReference(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
        });

        return modelBuilder;
    }

    public static ModelBuilder ConfigureSiteReference(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnalyticsSite>(entity =>
        {
            entity.ToTable("analytics_sites");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        return modelBuilder;
    }

    /// <summary>Call after the tables that point at users.</summary>
    public static ModelBuilder ConfigureUserReference(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("analytics_users");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        return modelBuilder;
    }
}
