using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace TempooAnalytics.Gateway.Tests;

/// <summary>
/// appsettings.json is the CORE route table: api, ingestion and web only, no private service and no real domain.
/// The private routes live in appsettings.Cloud.json (not part of the public tree).
/// </summary>
public sealed class CoreRouteTableTests
{
    private static IConfigurationRoot CoreConfig()
    {
        var dir = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
        return new ConfigurationBuilder().AddJsonFile(Path.Combine(dir, "appsettings.json"), optional: false).Build();
    }

    [Fact]
    public void CoreSettings_HaveOnlyApiIngestionHeatmapOverlayAndWeb()
    {
        var proxy = CoreConfig().GetSection("ReverseProxy");
        proxy.GetSection("Routes").GetChildren().Select(c => c.Key)
            .Should().BeEquivalentTo("api-route", "heatmap-overlay-route", "ingestion-route", "web-route");
        proxy.GetSection("Clusters").GetChildren().Select(c => c.Key)
            .Should().BeEquivalentTo("api-cluster", "ingestion-cluster", "web-cluster");
    }

    [Fact]
    public void CoreSettings_HaveNoHostsOrRealDomains()
    {
        var all = CoreConfig().AsEnumerable().Where(kv => kv.Value != null).ToList();
        all.Should().NotContain(kv => kv.Key.Contains(":Hosts"));
        all.Should().NotContain(kv => System.Text.RegularExpressions.Regex.IsMatch(kv.Value!, @"\.(eu|com|net|org|io)\b"));
    }
}
