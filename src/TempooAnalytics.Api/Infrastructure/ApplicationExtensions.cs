using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using TempooAnalytics.Api.Features.Analytics.Endpoints;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Infrastructure.FeatureFlags;
using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Features.Auth;
using TempooAnalytics.Api.Features.Auth.Services;
using TempooAnalytics.Api.Features.Auth.Endpoints;
using TempooAnalytics.Api.Features.Feedback;
using TempooAnalytics.Api.Features.Imports.Endpoints;
using TempooAnalytics.Api.Features.Notifications;
using TempooAnalytics.Api.Features.Test;
using TempooAnalytics.Api.Features.Heatmaps;
using TempooAnalytics.ServiceDefaults;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Modules;
using TempooAnalytics.Api.Infrastructure.Services;

namespace TempooAnalytics.Api.Infrastructure;

public static class ApplicationExtensions
{
    public static async Task AddApplication(this WebApplication app)
    {
        app.MapDefaultEndpoints();

        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.All,
            ForwardLimit = null
        };
        forwardedHeadersOptions.KnownIPNetworks.Clear();
        forwardedHeadersOptions.KnownProxies.Clear();
        app.UseForwardedHeaders(forwardedHeadersOptions);

        // Resolve the JWT signing key now so a missing or unwritable key stops the host at startup, not at the first login.
        app.Services.GetRequiredService<IJwtKeyProvider>().GetKey();

        app.UseCors("AllowFrontend");
        app.UseRateLimiter();
        app.UseSession();
        app.UseStaticFiles();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseAntiforgery();
        if (app.Environment.IsDevelopment() && app.Configuration["TEST_EXECUTION"] != "true")
        {
            app.MapOpenApi();
            app.MapScalarApiReference("/scalar/v1");
        }

        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
                var ex = exFeature?.Error;
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/problem+json";
                var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
                {
                    Title = "An unexpected error occurred.",
                    Status = StatusCodes.Status500InternalServerError,
                    Detail = app.Environment.IsDevelopment() && ex != null ? ex.ToString() : null
                };
                await context.Response.WriteAsJsonAsync(problem, AppJsonContext.Default.ProblemDetails);
            });
        });

        app.MapFeatureModuleRoots(app.Services);
        app.MapEndpoints();
        app.MapRazorPages();
        app.MapFallbackToFile("index.html");

        await app.InitializeDatabase();
    }

    private static void MapEndpoints(this WebApplication app)
    {
        app.MapGet("/", () => "Tempoo Analytics API is running.")
            .WithTags("System");

        var api = app.MapGroup("/api");
        
        api.MapGet("/healthz", () => "Tempoo Analytics API is running.")
            .WithTags("System")
            .RequireCors("PublicIngest");

        api.MapAuthEndpoints(app.Environment.IsDevelopment());
        api.MapUserAccountEndpoints();
        api.MapUserAdminEndpoints();
        api.MapSiteWriteKeyEndpoints();
        app.MapAnalyticsIngestEndpoint();
        api.MapAnalyticsDataEndpoints();
        api.MapDashboardEndpoints();
        api.MapSiteAdminEndpoints();
        api.MapPublicPreviewEndpoints();
        api.MapSiteMetricsEndpoints();
        api.MapFeatureFlagEndpoints();
        api.MapImportEndpoints();
        api.MapExportEndpoints();
        api.MapFunnelEndpoints();
        api.MapHeatmapEndpoints();
        api.MapFeedbackEndpoints();
        api.MapNotificationEndpoints();
        api.MapFeatureModules(app.Services);
        if (app.Environment.IsDevelopment()) api.MapTestEndpoints();
    }

    private static async Task InitializeDatabase(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
        var clickhouseMigration = scope.ServiceProvider.GetRequiredService<IClickHouseMigrationService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            if (app.Configuration["TEST_EXECUTION"] != "true") 
            {
                await CoreDatabaseMigrator.MigrateAsync(context, logger);
                try 
                {
                    await clickhouseMigration.MigrateAsync();

                    if (app.Environment.IsDevelopment())
                    {
                        await clickhouseMigration.SeedAsync();
                    }
                }
                catch (Exception ex) 
                {
                    logger.LogError(ex, "ClickHouse Migration/Seeding Error. Continuing with Postgres initialization...");
                    Console.WriteLine($">>> [CLICKHOUSE ERROR] {ex.Message}");
                }
            }

            // Modules that own database objects (the cloud's private tables) migrate and seed them now, after the core's own
            // migrations: their tables point at the core's. A community host registers none.
            var moduleContext = new ModuleStartupContext(app.Configuration, logger, app.Configuration["TEST_EXECUTION"] != "true", app.Environment.IsDevelopment());
            var moduleStartups = scope.ServiceProvider.GetServices<IModuleStartup>().ToList();
            foreach (var startup in moduleStartups)
                await startup.InitializeAsync(scope.ServiceProvider, moduleContext);

            if (!app.Environment.IsDevelopment())
            {
                // Self-host first run: an empty instance has no public sign-up, so create the first administrator (community only).
                await FirstAdminBootstrapper.RunAsync(context, scope.ServiceProvider.GetRequiredService<IUserManagementService>(),
                    app.Configuration, logger, Edition.IsCommunity(app.Services));
                return;
            }

            var devUserId = new UserId(Guid.Parse("00000000-0000-0000-0000-000000000001"));
            var devSiteId = new SiteId(Guid.Parse("10000000-0000-0000-0000-000000000001"));

            if (!await context.Users.AnyAsync(u => u.Id == devUserId))
            {
                context.Users.Add(new User { Id = devUserId, Username = "dev@tempoo.local", Email = "dev@tempoo.local", PasswordHash = PasswordHelper.HashPassword("dev"), IsAdmin = true, IsActive = true, CreatedAt = DateTime.UtcNow });
                await context.SaveChangesAsync();
                foreach (var startup in moduleStartups)
                    await startup.SeedDevelopmentUserAsync(scope.ServiceProvider, devUserId, moduleContext);
            }

            if (!await context.AnalyticsSites.AnyAsync(s => s.Id == devSiteId))
            {
                context.AnalyticsSites.Add(new AnalyticsSite { Id = devSiteId, SiteKey = "tempoo-local", Name = "Tempoo Local Dev", CreatedAt = DateTime.UtcNow, OwnerId = devUserId, WriteKey = "wk_dev_local_123456", AllowedOrigins = new[] { "localhost" } });
                context.UserSites.Add(new UserSite { UserId = devUserId, SiteId = devSiteId });
                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex) { logger.LogError(ex, "Database Initialization Error"); }
    }
}
