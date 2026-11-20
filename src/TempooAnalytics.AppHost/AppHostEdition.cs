using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

/// <summary>
/// What an edition adds to the local topology in <c>AppHost.cs</c> (database, ClickHouse, Redis, S3, ingestion, importer, gateway and
/// the frontend are the same in every edition). This file is the core on its own: the API is the core host, the frontend is the
/// community build, and nothing else is added. The cloud overlay, <c>AppHostEdition.Cloud.cs</c> (private tree only), replaces
/// this file in the build (see the .props overlay imported by the csproj) with the same members plus the cloud's services.
/// </summary>
internal sealed class AppHostEdition(IDistributedApplicationBuilder builder, string sentryOtlpEndpoint, IResourceBuilder<ParameterResource> sentryAuthHeader)
{
    /// <summary>The API project of the edition (the core host).</summary>
    public IResourceBuilder<ProjectResource> AddApi() => builder.AddProject<Projects.TempooAnalytics_Api>("tempooanalytics-api");

    /// <summary>Edition-specific settings of the ClickHouse container.</summary>
    public void ConfigureClickHouse(IResourceBuilder<ContainerResource> clickhouse) { }

    /// <summary>Edition-specific settings of the API resource.</summary>
    public void ConfigureApi(IResourceBuilder<ProjectResource> api) { }

    /// <summary>Services that sit next to the API (none in the core).</summary>
    public void AddApiServices(IResourceBuilder<ProjectResource> api, IResourceBuilder<PostgresDatabaseResource> database,
        IResourceBuilder<RedisResource> redis, IResourceBuilder<ContainerResource> clickhouse) { }

    /// <summary>References the gateway needs to the services of <see cref="AddApiServices"/>.</summary>
    public void ConfigureGateway(IResourceBuilder<ProjectResource> gateway) { }

    /// <summary>Edition-specific frontends other than the app (none in the core).</summary>
    public void AddLanding(IResourceBuilder<ProjectResource> gateway) { }

    /// <summary>The npm script that serves the app: the community build.</summary>
    public string WebScript => "start:community";
}
