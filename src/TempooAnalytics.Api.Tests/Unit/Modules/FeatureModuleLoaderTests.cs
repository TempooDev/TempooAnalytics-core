using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TempooAnalytics.Api.Infrastructure.Modules;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Modules;

public class FeatureModuleLoaderTests
{
    private sealed class Marker { }

    private sealed class RecordingModule : IFeatureModule
    {
        public List<string> Calls { get; } = [];
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            Calls.Add("services");
            services.AddSingleton<Marker>();
        }
        public void MapEndpoints(IEndpointRouteBuilder app) => Calls.Add("endpoints");
    }

    private sealed class FailingServicesModule : IFeatureModule
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) => throw new InvalidOperationException("boom");
        public void MapEndpoints(IEndpointRouteBuilder app) { }
    }

    private sealed class FailingMapModule : IFeatureModule
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
        public void MapEndpoints(IEndpointRouteBuilder app) => throw new InvalidOperationException("boom");
    }

    private static IConfiguration Config(string? modulesPath = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(modulesPath == null ? [] : new Dictionary<string, string?> { ["Modules:Path"] = modulesPath }).Build();

    [Fact]
    public void Discover_FindsModulesInExtraAssemblies()
    {
        // The test module classes above are private nested types with public default constructors.
        var modules = FeatureModuleLoader.Discover(Config(), [typeof(FeatureModuleLoaderTests).Assembly]);

        modules.Select(m => m.GetType()).Should().Contain(typeof(RecordingModule));
    }

    [Fact]
    public void Discover_DoesNotFail_WhenTheModulesDirectoryDoesNotExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"modules-{Guid.NewGuid()}");

        var act = () => FeatureModuleLoader.Discover(Config(missing));

        act.Should().NotThrow();
    }

    private static string ModulesDir(UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"modules-{Guid.NewGuid()}")).FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(dir, mode);
        return dir;
    }

    // What a host discovers with no modules directory: the modules of the assemblies it knows (the cloud ones when Api.Cloud is registered, none in the core alone).
    // A modules directory that adds nothing must leave exactly this.
    private static string[] Baseline() => FeatureModuleLoader.Discover(Config()).Select(m => m.GetType().FullName!).Order().ToArray();
    private static string[] Types(IEnumerable<IFeatureModule> modules) => modules.Select(m => m.GetType().FullName!).Order().ToArray();

    private static IConfiguration Config(string path, params string[] allowed)
    {
        var values = new Dictionary<string, string?> { ["Modules:Path"] = path };
        for (var i = 0; i < allowed.Length; i++) values[$"Modules:Allowed:{i}"] = allowed[i];
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Discover_DoesNotFail_WithAnEmptyDirectory()
    {
        var dir = ModulesDir();
        try { Types(FeatureModuleLoader.Discover(Config(dir))).Should().Equal(Baseline()); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Discover_RefusesADirectoryWithDlls_WhenThereIsNoAllowList()
    {
        var dir = ModulesDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "anything.dll"), "x");

            var act = () => FeatureModuleLoader.Discover(Config(dir));

            act.Should().Throw<InvalidOperationException>().WithMessage("*Modules:Allowed*");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Discover_IgnoresDllsThatAreNotAllowListedOrNotManaged()
    {
        var dir = ModulesDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "native.dll"), "not an assembly");
            File.Copy(typeof(Moq.Mock).Assembly.Location, Path.Combine(dir, "Moq.dll"));

            Types(FeatureModuleLoader.Discover(Config(dir, "SomeOtherModule"))).Should().Equal(Baseline());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Discover_RefusesADirectoryWritableByOthers()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = ModulesDir(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherWrite);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Module.dll"), "x");

            var act = () => FeatureModuleLoader.Discover(Config(dir, "Module"));

            act.Should().Throw<InvalidOperationException>().WithMessage("*writable by group or others*");
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>An assembly built at runtime with an IFeatureModule whose constructor throws.</summary>
    private static Assembly ThrowingConstructorAssembly()
    {
        var asm = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ThrowingModuleAssembly"), AssemblyBuilderAccess.Run);
        var type = asm.DefineDynamicModule("m").DefineType("Dynamic.ThrowingModule", TypeAttributes.Public | TypeAttributes.Class, typeof(object), [typeof(IFeatureModule)]);

        var ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldstr, "no deps");
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        foreach (var method in new[] { typeof(IFeatureModule).GetMethod(nameof(IFeatureModule.MapEndpoints))!, typeof(IFeatureModule).GetMethod(nameof(IFeatureModule.ConfigureServices), [typeof(IServiceCollection), typeof(IConfiguration)])! })
        {
            var impl = type.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                typeof(void), method.GetParameters().Select(p => p.ParameterType).ToArray());
            impl.GetILGenerator().Emit(OpCodes.Ret);
            type.DefineMethodOverride(impl, method);
        }
        type.CreateType();
        return asm;
    }

    [Fact]
    public void Discover_NamesTheModuleWhoseConstructorFails()
    {
        var act = () => FeatureModuleLoader.Discover(Config(), [ThrowingConstructorAssembly()]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Dynamic.ThrowingModule*could not be created*");
    }

    [Fact]
    public void AddFeatureModules_RegistersTheServicesAndTheCatalog()
    {
        var module = new RecordingModule();
        var services = new ServiceCollection();

        services.AddFeatureModules(Config(), [module]);

        module.Calls.Should().Equal("services");
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<Marker>().Should().NotBeNull();
        provider.GetRequiredService<FeatureModuleCatalog>().Modules.Should().ContainSingle().Which.Should().BeSameAs(module);
    }

    [Fact]
    public void AddFeatureModules_NamesTheModuleThatFailsInConfigureServices()
    {
        var act = () => new ServiceCollection().AddFeatureModules(Config(), [new FailingServicesModule()]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{typeof(FailingServicesModule).FullName}*ConfigureServices*")
            .WithInnerException<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public void MapFeatureModules_MapsEveryModule_AndNamesTheOneThatFails()
    {
        var recording = new RecordingModule();
        var services = new ServiceCollection().AddFeatureModules(Config(), [recording]);
        using var provider = services.BuildServiceProvider();
        var app = WebApplication.CreateBuilder().Build();

        app.MapFeatureModules(provider);
        recording.Calls.Should().Equal("services", "endpoints");

        var failing = new ServiceCollection().AddFeatureModules(Config(), [new FailingMapModule()]).BuildServiceProvider();
        var act = () => app.MapFeatureModules(failing);
        act.Should().Throw<InvalidOperationException>().WithMessage($"*{typeof(FailingMapModule).FullName}*MapEndpoints*");
    }

    private sealed class ContextModule : IFeatureModule
    {
        public ModuleContext? Seen { get; private set; }
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
        public void ConfigureServices(IServiceCollection services, ModuleContext context) => Seen = context;
        public void MapEndpoints(IEndpointRouteBuilder app) { }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddFeatureModules_PassesIsTestToTheModules(bool isTest)
    {
        var module = new ContextModule();
        var config = Config();

        new ServiceCollection().AddFeatureModules(config, [module], isTest);

        module.Seen.Should().Be(new ModuleContext(config, isTest));
    }

    [Fact]
    public void AddFeatureModules_ForwardsToTheSimpleOverload_WhenAModuleOnlyImplementsIt()
    {
        var module = new RecordingModule();

        new ServiceCollection().AddFeatureModules(Config(), [module], isTest: true);

        module.Calls.Should().Equal("services");
    }
}
