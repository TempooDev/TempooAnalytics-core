using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Where the cloud composition plugs into the shared test infrastructure (the Postgres fixture and the functional test host).
/// In the public tree nothing sets these, so the tests run against the core alone; in the private tree <c>CloudComposition.cs</c>
/// (private file) fills them in with the private chain and the private DbContext registration.
/// </summary>
public static class CloudTestHooks
{
    /// <summary>Migrates the cloud's own chain on the fixture's database (after the core chain) given its connection string.</summary>
    public static Func<string, Task>? MigrateAsync { get; set; }

    /// <summary>Registers the cloud's private DbContext on the functional test host (same per-scope connection as the core context).</summary>
    public static Action<IServiceCollection>? ConfigureServices { get; set; }
}
