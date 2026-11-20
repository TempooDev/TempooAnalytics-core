namespace TempooAnalytics.Api.Infrastructure.Modules;

/// <summary>What a module needs to know about the host when it registers its services.</summary>
/// <param name="Configuration">The application configuration.</param>
/// <param name="IsTest">True under the test host: modules must not register background workers or real external clients.</param>
/// <param name="EnvironmentName">The hosting environment (Development, Production...), for a module that must refuse a configuration in some of them; null when the caller has none (a test composing modules by hand).</param>
public sealed record ModuleContext(IConfiguration Configuration, bool IsTest, string? EnvironmentName = null);

/// <summary>
/// A pluggable slice of the API: it registers its own services and maps its own endpoints.
/// Implementations need a public parameterless constructor and are discovered by <see cref="FeatureModuleLoader"/>
/// in the API assembly, in any assembly passed explicitly and in the DLLs of the <c>Modules:Path</c> directory.
/// </summary>
public interface IFeatureModule
{
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// What the loader calls. Override it to read <see cref="ModuleContext.IsTest"/>; by default it forwards to
    /// <see cref="ConfigureServices(IServiceCollection, IConfiguration)"/> so simple modules only implement that one.
    /// </summary>
    void ConfigureServices(IServiceCollection services, ModuleContext context) => ConfigureServices(services, context.Configuration);

    /// <summary>Receives the <c>/api</c> group: map routes relative to it.</summary>
    void MapEndpoints(IEndpointRouteBuilder app);

    /// <summary>
    /// Receives the application root, not the <c>/api</c> group: for endpoints that cannot live under <c>/api</c>
    /// (a gRPC service, for example, whose path is fixed by its contract). Nothing by default.
    /// </summary>
    void MapRootEndpoints(IEndpointRouteBuilder app) { }
}
