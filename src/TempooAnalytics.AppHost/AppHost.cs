using Aspire.Hosting; // Ensure we have base types if needed
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var sentryOtlpEndpoint = "https://sentry.io/api/4511721546514512/otlp/";
var sentryAuthHeader = builder.AddParameter("SentryAuthHeader", secret: true);

// What this edition adds on top of the core topology (the cloud's extra services, the API project, the frontend script):
// see AppHostEdition.cs (the core alone) and AppHostEdition.Cloud.cs (private overlay, takes its place when present: TempooAnalytics.AppHost.Cloud.props).
var edition = new AppHostEdition(builder, sentryOtlpEndpoint, sentryAuthHeader);

var pg = builder.AddPostgres("Postgres")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPgAdmin();

var clickhousePassword = builder.Configuration["ClickHouse:Password"] ?? "tu_password_seguro";

// GeoLite2 CSVs for the ClickHouse dictionaries. MaxMind's licence forbids shipping them, so a one-shot container
// downloads them into deploy/clickhouse/user_files with the developer's own free account (same variables the
// API uses for the .mmdb). Optional: with no key (the default) it writes empty CSVs and the geo dictionaries stay
// empty. Set Parameters:MaxMindAccountId / Parameters:MaxMindLicenseKey in user secrets to get real data.
var maxMindAccountId = builder.AddParameter("MaxMindAccountId", builder.Configuration["Parameters:MaxMindAccountId"] ?? "", secret: true);
var maxMindLicenseKey = builder.AddParameter("MaxMindLicenseKey", builder.Configuration["Parameters:MaxMindLicenseKey"] ?? "", secret: true);
var geoipInit = builder.AddContainer("clickhouse-geoip-init", "alpine", "3.20")
    .WithBindMount("../../deploy/clickhouse/geoip", "/geoip/bin", isReadOnly: true)
    .WithBindMount("../../deploy/clickhouse/user_files", "/geoip/user_files")
    .WithEnvironment("MAXMIND_ACCOUNT_ID", maxMindAccountId)
    .WithEnvironment("MAXMIND_LICENSE_KEY", maxMindLicenseKey)
    .WithEntrypoint("/bin/sh")
    .WithArgs("-c", "apk add --no-cache curl unzip >/dev/null 2>&1; sh /geoip/bin/provision-csv.sh");

var clickhouse = builder.AddContainer("clickhouse", "clickhouse/clickhouse-server")
    .WithHttpEndpoint(port: 8123, targetPort: 8123, name: "http")
    // Host port 19000, not 9000: the local S3 endpoint below already takes 9000 on the host.
    .WithEndpoint(port: 19000, targetPort: 9000, name: "tcp")
    .WithEnvironment("CLICKHOUSE_USER", "api_worker")
    .WithEnvironment("CLICKHOUSE_PASSWORD", clickhousePassword)
    .WithBindMount("../../deploy/clickhouse/users.d", "/etc/clickhouse-server/users.d")
    .WithBindMount("../../deploy/clickhouse/docker-entrypoint-initdb.d", "/docker-entrypoint-initdb.d")
    .WithBindMount("../../deploy/clickhouse/user_files", "/var/lib/clickhouse-user-files") // Changed mount point
    .WithVolume("clickhouse-data-v4", "/var/lib/clickhouse") // New volume
    .WithVolume("clickhouse-logs", "/var/log/clickhouse-server")
    .WaitForCompletion(geoipInit);
edition.ConfigureClickHouse(clickhouse);

// Local S3 for exports and imports (R2 in production). SeaweedFS, because the minio/minio image is no longer
// published on Docker Hub. Without an -s3.config file SeaweedFS doesn't check credentials, so any key pair works
// locally and the presigned download links open as they are.
const string localS3AccessKey = "local";
const string localS3SecretKey = "local-secret";
var objectStorage = builder.AddContainer("s3", "chrislusf/seaweedfs", "4.53")
    .WithArgs("server", "-dir=/data", "-s3", "-s3.port=8333")
    .WithHttpEndpoint(port: 9000, targetPort: 8333, name: "s3")
    .WithVolume("s3-data", "/data");

var analyticsDb = pg.AddDatabase("analytics-db");
// Plain TCP locally: Aspire 13 enables TLS on Redis by default, but the Go services' ParseRedisOptions
// always connects without TLS, so they got EOF on every read from queue:imports.
#pragma warning disable ASPIRECERTIFICATES001 // Experimental API.
var redis = builder.AddRedis("redis")
    .WithoutHttpsCertificate();
#pragma warning restore ASPIRECERTIFICATES001
var analyticsApi = edition.AddApi()
    .WithReference(analyticsDb)
    .WithReference(redis)
    .WithEnvironment("ConnectionStrings__clickhouse", $"Host=127.0.0.1;Port=8123;Username=api_worker;Password={clickhousePassword};Database=tempoo_analytics;SocketTimeout=60000;MaxPoolSize=100")
    .WithEnvironment("Storage__R2__AccessKey", localS3AccessKey)
    .WithEnvironment("Storage__R2__SecretKey", localS3SecretKey)
    .WithEnvironment("Storage__R2__Endpoint", objectStorage.GetEndpoint("s3"))
    .WithEnvironment("Storage__R2__BucketName", "tempoo-exports")
    .WaitFor(analyticsDb)
    .WaitFor(redis)
    .WaitFor(clickhouse)
    .WaitFor(objectStorage)
    .WithOtlpExporter()
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", sentryOtlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_HEADERS", $"x-sentry-auth={sentryAuthHeader}");

edition.ConfigureApi(analyticsApi);
edition.AddApiServices(analyticsApi, analyticsDb, redis, clickhouse);

var ingestionService = builder.AddGolangApp("ingestion-service", "../TempooAnalytics.Go", "cmd/ingestion/main.go")
    .WithHttpEndpoint(targetPort: 8081, name: "http") // Single port for ingestion
    .WithEnvironment("CLICKHOUSE_HOST", clickhouse.GetEndpoint("http").Property(EndpointProperty.Host))
    .WithEnvironment("CLICKHOUSE_PORT", clickhouse.GetEndpoint("tcp").Property(EndpointProperty.Port))
    .WithEnvironment("CLICKHOUSE_USER", "api_worker")
    .WithEnvironment("CLICKHOUSE_PASSWORD", clickhousePassword)
    .WithEnvironment("CLICKHOUSE_DB", "tempoo_analytics")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", sentryOtlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_HEADERS", $"x-sentry-auth={sentryAuthHeader}")
    .WithReference(redis)
    .WithReference(analyticsDb)
    .WaitFor(clickhouse)
    .WaitFor(redis)
    .WaitFor(analyticsDb)
    .WithOtlpExporter();

var gateway = builder.AddProject<Projects.TempooAnalytics_Gateway>("gateway")
    .WithReference(analyticsApi)
    .WithReference(ingestionService)
    .WithHttpEndpoint(port: 8000, name: "http")
    // The gateway's catch-all route points at http://web (the production container name), which doesn't exist
    // locally and made every non-/api request fail with "nodename nor servname provided". Point it at the dev
    // server. Not WithReference(web): web already references the gateway, so that would be a cycle.
    .WithEnvironment("ReverseProxy__Clusters__web-cluster__Destinations__destination1__Address", "http://localhost:4200")
    .WithOtlpExporter();

edition.ConfigureGateway(gateway);

var importerService = builder.AddGolangApp("importer-service", "../TempooAnalytics.Go", "cmd/importer/main.go")
    .WithHttpEndpoint(targetPort: 8084, name: "http")
    .WithEnvironment("CLICKHOUSE_HOST", clickhouse.GetEndpoint("http").Property(EndpointProperty.Host))
    .WithEnvironment("CLICKHOUSE_PORT", clickhouse.GetEndpoint("tcp").Property(EndpointProperty.Port))
    .WithEnvironment("CLICKHOUSE_USER", "api_worker")
    .WithEnvironment("CLICKHOUSE_PASSWORD", clickhousePassword)
    .WithEnvironment("CLICKHOUSE_DB", "tempoo_analytics")
    .WithEnvironment("STORAGE_R2_ENDPOINT", objectStorage.GetEndpoint("s3"))
    .WithEnvironment("STORAGE_R2_ACCESS_KEY", localS3AccessKey)
    .WithEnvironment("STORAGE_R2_SECRET_KEY", localS3SecretKey)
    .WithEnvironment("STORAGE_R2_BUCKET", "tempoo-exports")
    .WithReference(redis)
    .WithReference(analyticsDb)
    .WaitFor(clickhouse)
    .WaitFor(redis)
    .WaitFor(analyticsDb)
    .WaitFor(objectStorage)
    .WithOtlpExporter();

// Frontend: the script of the edition's build (the core alone: the community build, main.ts).
var web = builder.AddJavaScriptApp("web", "../TempooAnalytics", edition.WebScript)
    .WithReference(gateway)
    .WaitFor(gateway)
    // Fixed port: the Google OAuth client registers the exact callback URL (see docs/google-oauth-setup.md).
    .WithHttpEndpoint(port: 4200, env: "PORT", name: "app")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

edition.AddLanding(gateway);

// Google Analytics 4 import (optional). Without Parameters:GoogleClientId the API still starts, logs a warning
// and the GA4 endpoints answer 503; the client secret is only asked for when the client id is set.
var googleClientId = builder.Configuration["Parameters:GoogleClientId"];
if (!string.IsNullOrWhiteSpace(googleClientId))
{
    var googleClientSecret = builder.AddParameter("GoogleClientSecret", secret: true);
    analyticsApi
        .WithEnvironment("Google__ClientId", googleClientId)
        .WithEnvironment("Google__ClientSecret", googleClientSecret)
        .WithEnvironment("Google__RedirectUri", ReferenceExpression.Create($"{web.GetEndpoint("app")}/account/imports/google/callback"));
}

builder.Build().Run();
