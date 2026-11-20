using Microsoft.AspNetCore.Cors.Infrastructure;
using TempooAnalytics.Api.Features.Billing;
using TempooAnalytics.Api.Infrastructure.Cors;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Infrastructure.Modules;
using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Analytics.EventHandlers;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Features.Auth;
using TempooAnalytics.Api.Features.Auth.Services;
using TempooAnalytics.Api.Features.Onboarding.Services;
using TempooAnalytics.Api.Infrastructure.Services;
using TempooAnalytics.Api.Infrastructure.Email;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Data;
using TempooAnalytics.Api.Infrastructure.Interceptors;
using TempooAnalytics.Api.Features.Outbox;
using TempooAnalytics.Api.Features.Imports.Services;
using TempooAnalytics.Api.Features.Imports.Ga4;
using TempooAnalytics.Api.Features.Notifications;
using TempooAnalytics.Api.Features.Notifications.Channels;
using TempooAnalytics.Api.Features.Heatmaps;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using TempooAnalytics.Api.Infrastructure.FeatureFlags;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TempooAnalytics.Api.Infrastructure;

public static class ServicesExtensions
{
    public static IServiceCollection AddServicesExtensions(this IServiceCollection services, IConfiguration configuration, bool isTest = false, string? environmentName = null)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
        });
        
        services.Configure<ResendOptions>(configuration.GetSection(ResendOptions.SectionName));
        services.Configure<AdminAlertOptions>(configuration.GetSection(AdminAlertOptions.SectionName));
        // Base URL of the links in emails (App:PublicUrl). The core default comes from this instance's configuration; the hosted
        // service sets its own in its module.
        services.Configure<PublicUrlOptions>(configuration.GetSection(PublicUrlOptions.SectionName));
        services.AddSingleton(sp => new EmailLinks(
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PublicUrlOptions>>(), configuration));
        services.PostConfigure<ResendOptions>(options => {
            if (string.IsNullOrEmpty(options.ApiKey)) {
                options.ApiKey = configuration["RESEND_APIKEY"] ?? configuration["Resend__ApiKey"] ?? "";
            }
        });

        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // Make sure it matches the refresh token
        });

        services.AddDatabaseServices(configuration, isTest);

        if (!isTest)
        {
            services.AddHostedService<OutboxProcessor>();
            services.AddHostedService<RetentionMaintenanceWorker>();
            services.AddHostedService<JobNotificationListener>();
            services.AddHostedService<Ga4DownloadService>();
        }

        services.AddEndpointsApiExplorer();
        services.AddOpenApi();
        services.AddValidators();
        services.AddAuthServices(configuration);
        services.AddCors(configuration);
        services.AddPublicRateLimits();
        services.AddRazorPages();
        services.AddAntiforgery();
        services.AddHttpContextAccessor();
        services.AddAutoRegisteredServices();
        services.AddGa4ImportServices(configuration);
        services.AddFeatureModules(configuration, isTest, environmentName: environmentName);

        return services;
    }

    private static void AddAutoRegisteredServices(this IServiceCollection services)
    {
        services.AddScoped<IFunnelService, FunnelService>();
        services.AddScoped<ISiteAccessService, SiteAccessService>();
        services.AddScoped<IHeatmapService, HeatmapService>();
        services.AddSingleton<IHeatmapOverlayTokenService, HeatmapOverlayTokenService>();
        services.AddScoped<IIngestionService, IngestionService>();
        services.AddScoped<IMetricsService, MetricsService>();
        services.AddScoped<IPublicPreviewService, PublicPreviewService>();
        services.AddScoped<ISiteService, SiteService>();
        services.AddCorePlanAndBillingDefaults();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.TryAddScoped<IUserLanguageResolver, DefaultUserLanguageResolver>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IExportService, ExportService>();
        services.AddScoped<IStorageService, R2StorageService>();
        services.AddSingleton<IStorageAvailability, StorageAvailability>();
        services.AddSingleton<IFeatureFlagContributor, StorageFeatureFlagContributor>();
        services.AddScoped<IAdminAlertService, AdminAlertService>();
        services.AddScoped<INotificationChannel, InAppNotificationChannel>();
        services.AddScoped<INotificationChannel, EmailNotificationChannel>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
        services.AddScoped<IJobNotifier, JobNotifier>();
        services.AddHttpClient<IEmailService, ResendEmailService>();
        services.AddSingleton<IGeolocationService, GeolocationService>();
        services.AddSingleton<IFeatureFlags, ConfigurationFeatureFlags>();
        services.AddScoped<IClickHouseMigrationService, ClickHouseMigrationService>();
    }

    private static void AddGa4ImportServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.Configure<GoogleOAuthOptions>(configuration.GetSection(GoogleOAuthOptions.SectionName));
        services.AddScoped<IImportQueue, RedisImportQueue>();
        services.AddSingleton<IGa4OAuthStore, Ga4OAuthStore>();
        services.AddSingleton<Ga4DownloadQueue>();
        services.AddScoped<Ga4ReportDownloader>();
        services.AddScoped<Ga4DownloadProcessor>();

        // Monthly reports of a large property can take a while, and the token exchange must not be retried
        // (an authorization code is single-use), so this client replaces the default resilience settings.
        // Default HttpClient logging only records URLs (no bodies, header values redacted).
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is marked experimental
        services.AddHttpClient<IGoogleAnalyticsClient, GoogleAnalyticsClient>()
            .RemoveAllResilienceHandlers()
            .AddResilienceHandler("google", pipeline =>
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = args => ValueTask.FromResult(
                        args.Outcome.Result?.RequestMessage?.RequestUri?.AbsoluteUri != GoogleAnalyticsClient.TokenEndpoint &&
                        (args.Outcome.Exception is HttpRequestException || (int?)args.Outcome.Result?.StatusCode >= 500))
                });
                pipeline.AddTimeout(TimeSpan.FromMinutes(2));
            });
#pragma warning restore EXTEXP0001
    }

    private static void AddDatabaseServices(this IServiceCollection services, IConfiguration configuration, bool isTest)
    {
        services.AddSingleton<AuditInterceptor>();

        // If we are in test, we do NOT register the DbContext here.
        // The test factory will do it (InMemory or Postgres Docker)
        if (!isTest && !services.Any(x => x.ServiceType == typeof(AnalyticsDbContext)))
        {
            var connectionString = configuration.GetConnectionString("analytics-db");
            // One connection per scope: a module with its own DbContext on the same database (the cloud's private tables) shares it, so a transaction can span both contexts.
            services.AddRequestDbConnection(connectionString!);
            services.AddDbContext<AnalyticsDbContext>((sp, options) =>
            {
                options.UseNpgsql(sp.GetRequiredService<RequestDbConnection>().Connection)
                       .UseSnakeCaseNamingConvention()
                       .AddInterceptors(sp.GetRequiredService<AuditInterceptor>())
                       .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            });
        }
        
        services.AddRepositories();
    }

    private static void AddValidators(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(ServicesExtensions).Assembly);
    }

    private static void AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IAnalyticsEventRepository, AnalyticsEventRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<ISiteRepository, SiteRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IFunnelRepository, FunnelRepository>();
        services.AddScoped<IHeatmapRepository, HeatmapRepository>();
        services.AddScoped<IMetricsRepository, MetricsRepository>();
        services.AddAnalyticsQueries(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("clickhouse")
            ?? throw new InvalidOperationException("ClickHouse connection string is missing"));
        services.AddScoped<IClickHouseRepository, ClickHouseRepository>();
    }

    private static void AddAuthServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthorization();
        services.AddAuthorizationBuilder()
            .AddPolicy("AdminPolicy", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("is_admin", "true");
            });

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

        // The signing key comes from IJwtKeyProvider (no public default outside Development; see JwtKeyProvider).
        services.AddSingleton<IJwtKeyProvider, JwtKeyProvider>();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IJwtKeyProvider>((options, keys) =>
                options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keys.GetKey())));
    }

    public const string LeadsRateLimitPolicy = "leads";

    // Public, unauthenticated forms. Partitioned by client IP; behind Cloudflare,
    // CF-Connecting-IP is the real client (X-Forwarded-For can be spoofed by the client).
    private static void AddPublicRateLimits(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LeadsRateLimitPolicy, context =>
            {
                var clientIp = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(10),
                    QueueLimit = 0
                });
            });
        });
    }

    private static void AddCors(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCors();
        // The core allows its own development hosts and whatever the operator lists in Cors:AllowedOrigins; a module (the hosted
        // service) adds its web hosts through ICorsOriginContributor. The policies are built here, with the contributors resolved.
        services.AddOptions<CorsOptions>().Configure<IEnumerable<ICorsOriginContributor>>((options, contributors) =>
        {
            var contributions = contributors.ToList();
            string[] devOrigins =
            [
                "http://localhost:4200",
                "http://localhost:4300",
                "http://localhost:4321",
                "https://localhost:60820",
                "http://localhost:60822",
            ];
            var configured = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            var frontendOrigins = devOrigins.Concat(configured).Concat(contributions.SelectMany(c => c.FrontendOrigins)).Distinct().ToArray();
            var landingOrigins = new[] { "http://localhost:4321" }.Concat(contributions.SelectMany(c => c.LandingOrigins)).Distinct().ToArray();

            options.AddPolicy("AllowFrontend", policy =>
            {
                policy
                    .WithOrigins(frontendOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });

            options.AddPolicy("PublicIngest", policy =>
            {
                // Called from any site (the tracker): any origin, never credentials (the key travels in the URL).
                policy
                    .AllowAnyOrigin()
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });

            options.AddPolicy("LandingOnly", policy =>
            {
                policy
                    .WithOrigins(landingOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });
    }
}
