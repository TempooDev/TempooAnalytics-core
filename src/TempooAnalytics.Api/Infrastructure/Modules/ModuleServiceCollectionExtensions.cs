using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;

namespace TempooAnalytics.Api.Infrastructure.Modules;

public static class ModuleServiceCollectionExtensions
{
    /// <summary>
    /// Makes the Razor pages compiled into <paramref name="assembly"/> part of the host. Pages are discovered by the framework
    /// from the host's entry assembly and its dependencies; a module that lives in another assembly (a test host, or one loaded
    /// from <c>Modules:Path</c>) calls this from its <c>ConfigureServices</c> so its pages exist wherever it is loaded. It is a
    /// no-op when the assembly is already a part (the cloud host is the module's own assembly, so the framework found it).
    /// </summary>
    public static IServiceCollection AddModulePages(this IServiceCollection services, Assembly assembly)
    {
        services.AddRazorPages().ConfigureApplicationPartManager(manager =>
        {
            var known = manager.ApplicationParts.Any(p =>
                (p is AssemblyPart a && a.Assembly == assembly) || (p is CompiledRazorAssemblyPart c && c.Assembly == assembly));
            if (known) return;
            var factory = ApplicationPartFactory.GetApplicationPartFactory(assembly);
            foreach (var part in factory.GetApplicationParts(assembly)) manager.ApplicationParts.Add(part);
        });
        return services;
    }
}
