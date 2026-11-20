using MaxMind.GeoIP2;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Modules;
using TempooAnalytics.ServiceDefaults;

namespace TempooAnalytics.Api;

/// <summary>
/// The API host as a function: <c>Program</c> of this project calls it, and so does the entry point of any host that composes the
/// API with more modules (it registers them first with <see cref="FeatureModuleLoader.RegisterModuleAssembly"/>). It is the same
/// code that used to be the top-level statements of <c>Program.cs</c>; only where it lives changed.
/// </summary>
public static class ApiHost
{
    public static async Task RunAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddBuilderExtensions();
        builder.Services.AddRouting();

        var isTest = builder.Configuration["TEST_EXECUTION"] == "true" || 
                     Environment.GetEnvironmentVariable("TEST_EXECUTION") == "true" ||
                     AppDomain.CurrentDomain.GetAssemblies().Any(a => a.FullName?.Contains("Microsoft.AspNetCore.Mvc.Testing") == true);

        var redisConn = builder.Configuration.GetConnectionString("redis");

        // Infrastructure setup - Skip entirely in tests
        if (!isTest)
        {
            if (!string.IsNullOrEmpty(redisConn))
            {
                builder.Services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(redisConn));
                builder.Services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = redisConn;
                    options.InstanceName = "TempooAnalytics-";
                });
            }
            else
            {
                builder.Services.AddDistributedMemoryCache();
            }

            // Data Protection with Redis
            if (!string.IsNullOrEmpty(redisConn))
            {
                builder.Services.AddDataProtection()
                    .PersistKeysToStackExchangeRedis(() => ConnectionMultiplexer.Connect(redisConn).GetDatabase(), "DataProtection-Keys")
                    .SetApplicationName("TempooAnalytics");
            }
        }
        else
        {
            // Test environment fallbacks
            builder.Services.AddDistributedMemoryCache();
            var dataProtectionPath = Path.Combine(Directory.GetCurrentDirectory(), "DataProtection-Keys");
            builder.Services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
                .SetApplicationName("TempooAnalytics");
        }

        builder.Services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(30),
                LocalCacheExpiration = TimeSpan.FromMinutes(5)
            };
        });

        // GeoLite2 country database: loaded from GeoIp:DataDirectory (default: the Data folder) and, when a MaxMind
        // account/key is configured, downloaded if missing or stale. Without either, country lookup is disabled.
        builder.Services.Configure<TempooAnalytics.Api.Infrastructure.GeoIpOptions>(o =>
        {
            var cfg = builder.Configuration;
            cfg.GetSection(TempooAnalytics.Api.Infrastructure.GeoIpOptions.Section).Bind(o);
            o.DataDirectory = FirstNonEmpty(cfg["GEOIP_DATA_DIR"], o.DataDirectory) ?? Path.Combine(builder.Environment.ContentRootPath, "Data");
            o.MaxMindAccountId = FirstNonEmpty(cfg["MAXMIND_ACCOUNT_ID"], o.MaxMindAccountId);
            o.MaxMindLicenseKey = FirstNonEmpty(cfg["MAXMIND_LICENSE_KEY"], o.MaxMindLicenseKey);
        });
        builder.Services.AddHttpClient(nameof(TempooAnalytics.Api.Infrastructure.GeoIpDatabaseProvisioner));
        builder.Services.AddSingleton<TempooAnalytics.Api.Infrastructure.GeoIpDatabase>();
        builder.Services.AddHostedService<TempooAnalytics.Api.Infrastructure.GeoIpDatabaseProvisioner>();

        // Add GeolocationService
        builder.Services.AddSingleton<IGeolocationService, GeolocationService>();

        var app = builder.Build();

        // AddApplication maps the default /health and /alive endpoints; mapping them here too made both routes ambiguous (500).
        await app.AddApplication();

        await app.RunAsync();
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
