using System.Collections.Generic;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TempooAnalytics.Api.Infrastructure.Email;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Services;

/// <summary>Email links come from this instance's configuration (App:PublicUrl), never from a fixed hosted-service host.</summary>
public class EmailLinksTests
{
    private static EmailLinks Links(PublicUrlOptions options, Dictionary<string, string?>? config = null) =>
        new(Options.Create(options), new ConfigurationBuilder().AddInMemoryCollection(config ?? new Dictionary<string, string?>()).Build());

    [Fact]
    public void PublicUrl_IsUsedForAppLinks_AndTheLogo()
    {
        var links = Links(new PublicUrlOptions { PublicUrl = "https://analytics.example.test/" });

        links.App("/auth/login").Should().Be("https://analytics.example.test/auth/login");
        links.LogoUrl.Should().Be("https://analytics.example.test/assets/images/tempoo-analytics-optimized.svg");
        links.Site("/en/docs").Should().Be("https://analytics.example.test/docs");
        links.SiteName.Should().Be("analytics.example.test");
    }

    [Fact]
    public void WithoutPublicUrl_FallsBackToTheFirstCorsOrigin_ThenToTheLocalGateway()
    {
        Links(new PublicUrlOptions(), new() { ["Cors:AllowedOrigins:0"] = "https://stats.example.test" })
            .App("/login").Should().Be("https://stats.example.test/login");
        Links(new PublicUrlOptions()).App("/login").Should().Be("http://localhost:8000/login");
    }

    [Fact]
    public void SiteUrl_AndLogo_CanBeSetSeparately()
    {
        var links = Links(new PublicUrlOptions
        {
            PublicUrl = "https://app.example.test",
            SiteUrl = "https://example.test",
            EmailLogoUrl = "https://cdn.example.test/logo.png",
        });

        links.Site("/en/docs").Should().Be("https://example.test/en/docs");
        links.SiteName.Should().Be("example.test");
        links.LogoUrl.Should().Be("https://cdn.example.test/logo.png");
    }
}
