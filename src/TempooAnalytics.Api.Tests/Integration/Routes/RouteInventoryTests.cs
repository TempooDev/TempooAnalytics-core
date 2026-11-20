using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TempooAnalytics.Api.Infrastructure.Modules;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Routes;

/// <summary>
/// The route table of a host, committed as text: method, pattern, endpoint name, tags and what authorization the route asks for.
/// It is the "behavior did not change" check for moving code between projects (open-source-core, Api.Cloud): the same routes
/// must exist before and after a move, in the community host (<c>Modules:Enabled=false</c>, committed in <c>routes.community.txt</c>,
/// public) and in the cloud host (<c>routes.cloud.txt</c>, private: only the cloud composition registers its modules).
///
/// To rewrite a committed file after an intended change: run the test with <c>ROUTES_BASELINE_DIR</c> set to the folder that holds it
/// (<c>src/TempooAnalytics.Api.Tests/Integration/Routes</c>) and review the diff.
/// </summary>
public class RouteInventoryTests : FunctionalTestBase
{
    public RouteInventoryTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public void Community_HasExactlyTheCommittedRoutes() =>
        Check("routes.community.txt", Inventory(modules: false));

    /// <summary>
    /// The cloud composition (the test assembly registers it, see CloudModules). Skipped where it does not exist (the public tree).
    /// </summary>
    [Fact]
    public void Cloud_HasExactlyTheCommittedRoutes()
    {
        if (!Assembly.GetExecutingAssembly().GetManifestResourceNames().Any(n => n.EndsWith("routes.cloud.txt", StringComparison.Ordinal)))
            return; // public tree: no cloud composition, no cloud golden file
        Check("routes.cloud.txt", Inventory(modules: true));
    }

    private string Inventory(bool modules)
    {
        using var factory = Factory.WithWebHostBuilder(b => b.UseSetting(FeatureModuleLoader.EnabledSetting, modules ? "true" : "false"));
        var services = factory.Services;
        var lines = services.GetServices<EndpointDataSource>().SelectMany(ds => ds.Endpoints).OfType<RouteEndpoint>()
            .Select(Describe)
            .Distinct()
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();
        var loaded = services.GetRequiredService<FeatureModuleCatalog>().Modules.Select(m => m.GetType().Name).OrderBy(n => n, StringComparer.Ordinal);
        return "# modules: " + string.Join(", ", loaded) + "\n# community: " + Edition.IsCommunity(services) + "\n" + string.Join("\n", lines) + "\n";
    }

    private static string Describe(RouteEndpoint e)
    {
        var methods = string.Join(",", e.Metadata.OfType<IHttpMethodMetadata>().SelectMany(m => m.HttpMethods).Distinct().OrderBy(m => m, StringComparer.Ordinal));
        var name = e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? "-";
        var tags = string.Join(",", e.Metadata.OfType<ITagsMetadata>().SelectMany(t => t.Tags).Distinct().OrderBy(t => t, StringComparer.Ordinal));
        var auth = string.Join(",", e.Metadata.OfType<IAuthorizeData>().Select(a => string.IsNullOrEmpty(a.Policy) ? "authorize" : $"policy:{a.Policy}").Distinct().OrderBy(a => a, StringComparer.Ordinal));
        if (e.Metadata.GetMetadata<IAllowAnonymous>() != null) auth = (auth.Length == 0 ? "" : auth + ",") + "anonymous";
        return $"{(methods.Length == 0 ? "*" : methods)} {e.RoutePattern.RawText} | name={name} | tags={tags} | auth={(auth.Length == 0 ? "-" : auth)}";
    }

    private static void Check(string file, string actual)
    {
        var dir = Environment.GetEnvironmentVariable("ROUTES_BASELINE_DIR");
        if (!string.IsNullOrEmpty(dir))
        {
            File.WriteAllText(Path.Combine(dir, file), actual);
            return;
        }
        var assembly = typeof(RouteInventoryTests).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith(file, StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        var expected = reader.ReadToEnd().Replace("\r\n", "\n");

        var missing = expected.Split('\n').Except(actual.Split('\n')).Where(l => l.Length > 0).ToList();
        var added = actual.Split('\n').Except(expected.Split('\n')).Where(l => l.Length > 0).ToList();
        (missing.Concat(added)).Should().BeEmpty(
            $"{file} is the committed route table. Missing now: [{string.Join(" ;; ", missing)}]. New: [{string.Join(" ;; ", added)}]. " +
            "If the change is intended, rerun with ROUTES_BASELINE_DIR to rewrite the file.");
    }
}
