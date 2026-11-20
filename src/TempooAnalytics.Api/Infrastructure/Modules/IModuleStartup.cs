using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Infrastructure.Modules;

/// <summary>What a module's startup step needs to know about the host.</summary>
/// <param name="Configuration">The application configuration.</param>
/// <param name="Logger">The host's logger; a failure of a module's step is logged by the caller and never stops the host.</param>
/// <param name="MigrateDatabase">False under the test host (<c>TEST_EXECUTION=true</c>): the test fixture owns the migrations there, so a module must only seed.</param>
/// <param name="IsDevelopment">True in the Development environment.</param>
public sealed record ModuleStartupContext(IConfiguration Configuration, ILogger Logger, bool MigrateDatabase, bool IsDevelopment);

/// <summary>
/// A module that owns database objects and has to prepare them when the host starts: apply its own migrations (a module with its own
/// <c>DbContext</c> and migration history) and seed its reference data. The core runs every registered step right after its own
/// migrations, so a module's tables can point at the core's. A host without the module (the community edition) registers none,
/// so it never touches the module's schemas. Register it from <see cref="IFeatureModule.ConfigureServices(IServiceCollection, ModuleContext)"/>
/// with <c>TryAddEnumerable</c> (several modules of one assembly can share a step).
/// </summary>
public interface IModuleStartup
{
    /// <summary>Migrates the module's database objects (when <see cref="ModuleStartupContext.MigrateDatabase"/>) and seeds its reference data. Runs in a request-like scope.</summary>
    Task InitializeAsync(IServiceProvider services, ModuleStartupContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Development only: the core has just created and saved the local development user; a module may attach its own data to it (a plan, for example).
    /// Nothing by default.
    /// </summary>
    Task SeedDevelopmentUserAsync(IServiceProvider services, UserId userId, ModuleStartupContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
