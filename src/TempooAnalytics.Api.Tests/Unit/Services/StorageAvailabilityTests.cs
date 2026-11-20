using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using TempooAnalytics.Api.Infrastructure.FeatureFlags;
using TempooAnalytics.Api.Infrastructure.Services;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Services;

/// <summary>Whether imports and exports can work on this server: object storage (S3/R2) configured, or an environment that falls back to a local MinIO.</summary>
public class StorageAvailabilityTests
{
    // In-memory only: the process' own environment variables (CI, ASPNETCORE_ENVIRONMENT) must not leak into the cases.
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    private static readonly (string, string)[] FullCredentials =
    [
        ("Storage:R2:Endpoint", "https://s3.example.test"), ("Storage:R2:AccessKey", "ak"), ("Storage:R2:SecretKey", "sk"),
    ];

    [Fact]
    public void Community_WithoutCredentialsInProduction_IsUnavailable()
    {
        StorageAvailability.Evaluate(Config(("ASPNETCORE_ENVIRONMENT", "Production")), community: true).Should().BeFalse();
        StorageAvailability.Evaluate(Config(), community: true).Should().BeFalse();
    }

    [Fact]
    public void Community_WithEmptyOrPartialCredentials_IsUnavailable()
    {
        StorageAvailability.Evaluate(Config(("Storage:R2:Endpoint", ""), ("Storage:R2:AccessKey", ""), ("Storage:R2:SecretKey", "")), community: true).Should().BeFalse();
        StorageAvailability.Evaluate(Config(("Storage:R2:Endpoint", "https://s3.example.test"), ("Storage:R2:AccessKey", "ak")), community: true).Should().BeFalse();
    }

    [Fact]
    public void Community_WithTheThreeSettings_IsAvailable() =>
        StorageAvailability.Evaluate(Config(FullCredentials), community: true).Should().BeTrue();

    [Theory]
    [InlineData("ASPNETCORE_ENVIRONMENT", "Development")]
    [InlineData("ASPNETCORE_ENVIRONMENT", "Testing")]
    [InlineData("TEST_EXECUTION", "true")]
    [InlineData("CI", "true")]
    [InlineData("GITHUB_ACTIONS", "true")]
    public void Community_InDevelopmentTestOrCi_FallsBackToTheLocalStorageAsTheStorageServiceDoes(string key, string value) =>
        StorageAvailability.Evaluate(Config((key, value)), community: true).Should().BeTrue();

    [Fact]
    public void Cloud_IsAlwaysAvailable_ItsStorageServiceStillRefusesToStartWithoutCredentials()
    {
        StorageAvailability.Evaluate(Config(("ASPNETCORE_ENVIRONMENT", "Production")), community: false).Should().BeTrue();
    }

    [Fact]
    public void FlagContributor_AddsDataStorageUnavailable_OnlyWhenStorageIsMissing()
    {
        var missing = new Mock<IStorageAvailability>();
        missing.SetupGet(s => s.IsAvailable).Returns(false);
        var present = new Mock<IStorageAvailability>();
        present.SetupGet(s => s.IsAvailable).Returns(true);

        new StorageFeatureFlagContributor(missing.Object).Flags.Should().BeEquivalentTo(new Dictionary<string, bool> { ["dataStorageUnavailable"] = true });
        new StorageFeatureFlagContributor(present.Object).Flags.Should().BeEmpty();
    }

    [Fact]
    public void FeaturesResponse_OfAHostWithStorage_HasNoNewKey()
    {
        var present = new Mock<IStorageAvailability>();
        present.SetupGet(s => s.IsAvailable).Returns(true);
        var flags = new ConfigurationFeatureFlags(Config());

        var response = FeatureFlagEndpoints.BuildResponse(flags, [new StorageFeatureFlagContributor(present.Object)]);

        response.Keys.Should().BeEquivalentTo(["plausibleImport", "ga4Import", "notifications", "heatmaps"]);
    }

    [Fact]
    public void FeaturesResponse_OfAHostWithoutStorage_ReportsIt()
    {
        var missing = new Mock<IStorageAvailability>();
        missing.SetupGet(s => s.IsAvailable).Returns(false);
        var flags = new ConfigurationFeatureFlags(Config());

        var response = FeatureFlagEndpoints.BuildResponse(flags, [new StorageFeatureFlagContributor(missing.Object)]);

        response["dataStorageUnavailable"].Should().BeTrue();
    }
}
