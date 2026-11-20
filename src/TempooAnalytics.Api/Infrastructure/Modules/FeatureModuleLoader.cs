using System.Reflection;
using System.Runtime.Loader;

namespace TempooAnalytics.Api.Infrastructure.Modules;

/// <summary>Modules found at startup; registered as a singleton so the endpoint mapping can reuse the same instances.</summary>
public sealed class FeatureModuleCatalog(IReadOnlyList<IFeatureModule> modules)
{
    public IReadOnlyList<IFeatureModule> Modules { get; } = modules;
}

public static class FeatureModuleLoader
{
    public const string PathSetting = "Modules:Path";

    /// <summary>
    /// Assembly names (without ".dll") that may be loaded from <c>Modules:Path</c>. Loading a DLL runs its code with the
    /// privileges of the API, so a directory with DLLs and no allow-list is refused instead of loaded.
    /// </summary>
    public const string AllowedSetting = "Modules:Allowed";

    private static readonly object RegisteredLock = new();
    private static readonly List<Assembly> Registered = [];

    /// <summary>
    /// Adds an assembly the host was compiled against to the places modules are searched, next to the API assembly. This is how a
    /// host that references more module projects (the cloud composition, or a test assembly) says which ones: it calls this once,
    /// before the host is built, from its entry point (or a module initializer). Unlike <c>Modules:Path</c> it needs no allow-list,
    /// because the assembly is a compile-time reference, not a file somebody dropped in a directory. <c>Modules:Enabled=false</c> still wins.
    /// </summary>
    public static void RegisterModuleAssembly(Assembly assembly)
    {
        lock (RegisteredLock)
            if (!Registered.Contains(assembly)) Registered.Add(assembly);
    }

    private static Assembly[] RegisteredAssemblies()
    {
        lock (RegisteredLock) return Registered.ToArray();
    }

    /// <summary>Set <c>Modules:Enabled</c> to false to run the core alone: no module is discovered or loaded (default true).</summary>
    public const string EnabledSetting = "Modules:Enabled";

    /// <summary>
    /// Finds every concrete <see cref="IFeatureModule"/> in the API assembly, in the assemblies registered with <see cref="RegisterModuleAssembly"/>, in <paramref name="extraAssemblies"/>
    /// and in the DLLs of <c>Modules:Path</c> (a missing or empty directory is fine; see <see cref="AllowedSetting"/>). Order is deterministic.
    /// </summary>
    public static IReadOnlyList<IFeatureModule> Discover(IConfiguration configuration, IEnumerable<Assembly>? extraAssemblies = null)
    {
        if (!configuration.GetValue(EnabledSetting, true)) return [];

        var assemblies = new List<Assembly> { typeof(IFeatureModule).Assembly };
        assemblies.AddRange(RegisteredAssemblies());
        if (extraAssemblies != null) assemblies.AddRange(extraAssemblies);
        assemblies.AddRange(LoadFromDirectory(configuration[PathSetting], configuration.GetSection(AllowedSetting).Get<string[]>() ?? []));

        return assemblies
            .Distinct()
            .SelectMany(SafeGetTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IFeatureModule).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
            .Distinct()
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(Create)
            .ToList();
    }

    /// <summary>Discovers the modules, lets each register its services and adds the catalog to the container.</summary>
    public static IServiceCollection AddFeatureModules(this IServiceCollection services, IConfiguration configuration, bool isTest = false, IEnumerable<Assembly>? extraAssemblies = null, string? environmentName = null)
    {
        return services.AddFeatureModules(configuration, Discover(configuration, extraAssemblies), isTest, environmentName);
    }

    /// <summary>Lets each given module register its services and adds the catalog to the container.</summary>
    public static IServiceCollection AddFeatureModules(this IServiceCollection services, IConfiguration configuration, IReadOnlyList<IFeatureModule> modules, bool isTest = false, string? environmentName = null)
    {
        var context = new ModuleContext(configuration, isTest, environmentName);
        foreach (var module in modules)
        {
            try { module.ConfigureServices(services, context); }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Feature module {module.GetType().FullName} failed in ConfigureServices.", ex);
            }
        }
        services.AddSingleton(new FeatureModuleCatalog(modules));
        return services;
    }

    /// <summary>Maps the endpoints of every module found by <see cref="AddFeatureModules"/>.</summary>
    public static void MapFeatureModules(this IEndpointRouteBuilder app, IServiceProvider services)
    {
        foreach (var module in services.GetRequiredService<FeatureModuleCatalog>().Modules)
        {
            try { module.MapEndpoints(app); }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Feature module {module.GetType().FullName} failed in MapEndpoints.", ex);
            }
        }
    }

    /// <summary>Maps the root-level endpoints (outside <c>/api</c>) of every module found by <see cref="AddFeatureModules"/>.</summary>
    public static void MapFeatureModuleRoots(this IEndpointRouteBuilder app, IServiceProvider services)
    {
        foreach (var module in services.GetRequiredService<FeatureModuleCatalog>().Modules)
        {
            try { module.MapRootEndpoints(app); }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Feature module {module.GetType().FullName} failed in MapRootEndpoints.", ex);
            }
        }
    }

    private static IFeatureModule Create(Type type)
    {
        try { return (IFeatureModule)Activator.CreateInstance(type)!; }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Feature module {type.FullName} could not be created.", ex);
        }
    }

    /// <summary>
    /// Loads the allow-listed DLLs of <paramref name="path"/>. The directory must not be writable by group or others
    /// (Unix): whoever can drop a DLL there runs code inside the API. Other DLLs (dependencies) are left alone.
    /// </summary>
    private static IEnumerable<Assembly> LoadFromDirectory(string? path, IReadOnlyCollection<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) yield break;

        var files = Directory.EnumerateFiles(path, "*.dll").OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (files.Count == 0) yield break;

        if (allowed.Count == 0)
            throw new InvalidOperationException($"{PathSetting} contains DLLs but {AllowedSetting} is empty: list the module assemblies that may be loaded.");

        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
            throw new InvalidOperationException($"{PathSetting} '{path}' is writable by group or others; it must be read-only for everyone but its owner.");

        foreach (var file in files)
        {
            string? name;
            try { name = AssemblyName.GetAssemblyName(file).Name; }
            catch (BadImageFormatException) { continue; } // not a managed assembly
            if (name == null || !allowed.Contains(name, StringComparer.Ordinal)) continue;

            Assembly? assembly = null;
            try { assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(file)); }
            catch (FileLoadException) { /* same assembly already loaded */ }
            if (assembly != null) yield return assembly;
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
    }
}
