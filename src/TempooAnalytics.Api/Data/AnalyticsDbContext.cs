using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Infrastructure.Common;
using CommonId = TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Features.Imports.Models;
using TempooAnalytics.Api.Features.Notifications.Models;
using TempooAnalytics.Api.Features.Feedback.Models;

namespace TempooAnalytics.Api.Data;

public class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
    public virtual DbSet<AnalyticsSite> AnalyticsSites { get; set; }
    public virtual DbSet<AdminWriteKeyAudit> AdminWriteKeyAudits { get; set; }
    public virtual DbSet<AuditLog> AuditLogs { get; set; }
    public virtual DbSet<OutboxMessage> OutboxMessages { get; set; }
    public virtual DbSet<User> Users { get; set; }
    public virtual DbSet<UserSite> UserSites { get; set; }
    public virtual DbSet<FunnelDefinition> Funnels { get; set; }
    public virtual DbSet<Feedback> Feedbacks { get; set; }
    public virtual DbSet<ImportJob> ImportJobs { get; set; }
    public virtual DbSet<ExportJob> ExportJobs { get; set; }
    public virtual DbSet<Notification> Notifications { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User and AnalyticsSite are shared with the private DbContext, which references them from its foreign keys.
        modelBuilder.ConfigureCoreReferenceConverters().ConfigureSiteReference().ConfigureOutboxReference();

        // Strongly Typed ID Converters

        modelBuilder.Entity<UserSite>().Property(e => e.UserId).HasConversion(id => id.Value, v => new UserId(v));
        modelBuilder.Entity<UserSite>().Property(e => e.SiteId).HasConversion(id => id.Value, v => new SiteId(v));

        modelBuilder.Entity<FunnelDefinition>().Property(e => e.SiteId).HasConversion(id => id.Value, v => new SiteId(v));

        modelBuilder.Entity<Feedback>().Property(e => e.UserId).HasConversion(id => id.Value, v => new UserId(v));
        modelBuilder.Entity<ImportJob>().Property(e => e.SiteId).HasConversion(id => id.Value, v => new SiteId(v));
        modelBuilder.Entity<ImportJob>().Property(e => e.CreatedByUserId).HasConversion(id => id.HasValue ? id.Value.Value : (Guid?)null, v => v.HasValue ? new UserId(v.Value) : null);

        modelBuilder.Entity<Features.Analytics.Models.ExportJob>().Property(e => e.SiteId).HasConversion(id => id.Value, v => new SiteId(v));
        modelBuilder.Entity<Features.Analytics.Models.ExportJob>().Property(e => e.UserId).HasConversion(id => id.Value, v => new UserId(v));

        modelBuilder.Entity<ImportJob>(entity =>
        {
            entity.ToTable("import_jobs", "jobs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Features.Analytics.Models.ExportJob>(entity =>
        {
            entity.ToTable("export_jobs", "jobs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications", "notifications");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.UserId).HasConversion(id => id.Value, v => new UserId(v));
            entity.Property(e => e.SiteId).HasConversion(id => id.HasValue ? id.Value.Value : (Guid?)null, v => v.HasValue ? new SiteId(v.Value) : null);
            entity.Property(e => e.Type).HasMaxLength(64);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.Body).HasMaxLength(1000);
            entity.Property(e => e.Link).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");

            // One notification per user, type and origin (queue messages can be repeated).
            entity.HasIndex(e => new { e.UserId, e.Type, e.SourceId })
                .IsUnique()
                .HasFilter("source_id IS NOT NULL");
            entity.HasIndex(e => new { e.UserId, e.CreatedAt, e.Id })
                .IsDescending(false, true, true);
            entity.HasIndex(e => e.UserId)
                .HasFilter("read_at IS NULL")
                .HasDatabaseName("ix_notifications_user_id_unread");
            // Daily retention delete (created_at < now - 90 days).
            entity.HasIndex(e => e.CreatedAt);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AnalyticsSite>()
                .WithMany()
                .HasForeignKey(e => e.SiteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Feedback>(entity =>
        {
            entity.ToTable("analytics_feedbacks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<AdminWriteKeyAudit>(entity =>
        {
            entity.ToTable("admin_write_key_audit");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
        });

        // Only core tables are mapped here. The tables of the feature modules (billing, AI, public API, marketing) belong to the
        // private DbContext of the cloud composition (Api.Cloud), which references users and sites as foreign-key targets.
        modelBuilder.ConfigureUserReference();

        modelBuilder.Entity<UserSite>(entity =>
        {
            entity.ToTable("analytics_user_sites");
            entity.HasKey(e => new { e.UserId, e.SiteId });
        });

        modelBuilder.Entity<FunnelDefinition>(entity =>
        {
            entity.ToTable("analytics_funnels");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
        });

    }
}
