using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Features.Notifications;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Modules;

namespace TempooAnalytics.Api.Features.Analytics.Services;

public sealed class RetentionMaintenanceWorker(
    IServiceProvider serviceProvider,
    ILogger<RetentionMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RetentionMaintenanceWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Run once a day at 3 AM or on startup
                await PerformCleanupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during retention maintenance execution.");
            }

            try
            {
                await CleanupNotificationsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting old notifications.");
            }

            // Wait 24 hours before next run
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task CleanupNotificationsAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
        var deleted = await NotificationRetention.DeleteExpiredAsync(dbContext, DateTime.UtcNow, ct);
        logger.LogInformation("Deleted {Count} notifications older than {Days} days.", deleted, NotificationRetention.MaxAge.TotalDays);
    }

    /// <summary>Setting that bounds retention in the community edition, in months. Unset or 0 = keep everything.</summary>
    public const string CommunityRetentionSetting = "Retention:Months";

    public async Task PerformCleanupAsync(CancellationToken ct)
    {
        // Community has no plans: with no explicit setting, data is kept forever. Falling through to the tier
        // lookup would apply the seeded "free" tier (3 months) to every self-hosted user and silently delete their data.
        int? communityMonths = null;
        if (Edition.IsCommunity(serviceProvider))
        {
            communityMonths = serviceProvider.GetService<IConfiguration>()?.GetValue<int?>(CommunityRetentionSetting);
            if (communityMonths is not > 0)
            {
                logger.LogInformation("Community edition without {Setting}: retention is unlimited, skipping data cleanup.", CommunityRetentionSetting);
                return;
            }
        }

        logger.LogInformation("Starting data retention cleanup...");

        using var scope = serviceProvider.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var planStore = scope.ServiceProvider.GetRequiredService<IUserPlanStore>();
        var clickHouseRepo = scope.ServiceProvider.GetRequiredService<IClickHouseRepository>();

        var users = await userRepo.GetAllUsersAsync();
        int totalDeleted = 0;

        foreach (var user in users)
        {
            if (ct.IsCancellationRequested) break;

            var tierSlug = "community";
            int retentionMonths;
            if (communityMonths is { } cm)
            {
                retentionMonths = cm;
            }
            else
            {
                var plan = await planStore.GetPlanAsync(user.Id);
                if (plan == null) continue;
                retentionMonths = plan.RetentionMonths;
                tierSlug = plan.Slug;
            }
            var cutoffDate = DateTime.UtcNow.AddMonths(-retentionMonths);

            var siteIds = await userRepo.ListSitesForUserAsync(user.Id);

            foreach (var siteId in siteIds)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    logger.LogInformation("Cleaning up data for site {SiteId} (Tier: {Tier}, Retention: {Months} months, Cutoff: {Cutoff})", 
                        siteId, tierSlug, retentionMonths, cutoffDate.ToShortDateString());
                    
                    await clickHouseRepo.CleanupOldDataAsync(siteId, cutoffDate, ct);
                    totalDeleted++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to cleanup site {SiteId}", siteId);
                }
            }
        }

        logger.LogInformation("Retention maintenance completed. Processed {Count} sites.", totalDeleted);
    }
}
