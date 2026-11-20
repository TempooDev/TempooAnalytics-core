using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using TempooAnalytics.Api.Infrastructure.FeatureFlags;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.FeatureFlags;

public class FeatureFlagsEndpointTests
{
    private static IConfiguration Config(params string[] enabled) =>
        new ConfigurationBuilder().AddInMemoryCollection(enabled.ToDictionary(f => $"FeatureFlags:{f}", _ => (string?)"true")).Build();

    // With the cloud modules loaded the response also carries their flags (aiCredits, publicApi...): see FeatureFlagsCloudModulesTests (private).
    [Fact]
    public void Response_WithoutModules_OnlyHasTheCoreFlags()
    {
        var response = FeatureFlagEndpoints.BuildResponse(new ConfigurationFeatureFlags(Config("Notifications")), []);

        response.Keys.Should().BeEquivalentTo(["plausibleImport", "ga4Import", "notifications", "heatmaps"]);
        response["notifications"].Should().BeTrue();
    }
}
