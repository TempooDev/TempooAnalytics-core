using System;
using AwesomeAssertions;
using TempooAnalytics.Api.Features.Heatmaps;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Heatmaps;

public class HeatmapOverlayOriginsTests
{
    [Theory]
    [InlineData("https://tienda.es", true)]
    [InlineData("https://www.tienda.es", true)]
    [InlineData("https://blog.tienda.es", true)]
    [InlineData("https://tienda.es.evil.com", false)]
    [InlineData("https://eviltienda.es", false)]
    [InlineData("https://otra-web.com", false)]
    [InlineData("null", false)]
    [InlineData("", false)]
    public void IsAllowed_BareHostEntry(string origin, bool expected)
    {
        HeatmapOverlayOrigins.IsAllowed(origin, ["tienda.es"], "Mi tienda").Should().Be(expected);
    }

    [Fact]
    public void IsAllowed_EntryWithScheme_MustMatchExactly()
    {
        string[] allowed = ["https://tienda.es"];
        HeatmapOverlayOrigins.IsAllowed("https://tienda.es", allowed, null).Should().BeTrue();
        HeatmapOverlayOrigins.IsAllowed("http://tienda.es", allowed, null).Should().BeFalse();
        HeatmapOverlayOrigins.IsAllowed("https://blog.tienda.es", allowed, null).Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_WithoutAllowedOrigins_UsesTheDomainInTheSiteName()
    {
        HeatmapOverlayOrigins.IsAllowed("https://tienda.es", [], "tienda.es").Should().BeTrue();
        HeatmapOverlayOrigins.IsAllowed("https://otra.es", [], "tienda.es").Should().BeFalse();
        HeatmapOverlayOrigins.IsAllowed("https://tienda.es", null, "Mi tienda").Should().BeFalse();
    }

    [Theory]
    [InlineData(new[] { "tienda.es" }, "Mi tienda", "https://tienda.es")]
    [InlineData(new[] { "https://www.tienda.es/" }, "x", "https://www.tienda.es")]
    [InlineData(new string[0], "tienda.es", "https://tienda.es")]
    [InlineData(new string[0], "Mi tienda", null)]
    public void SiteUrl_FromOriginsOrName(string[] allowed, string name, string? expected)
    {
        HeatmapOverlayOrigins.SiteUrl(allowed, name).Should().Be(expected);
    }

    [Fact]
    public void IsSameSite_MatchesTheSiteIdOrItsKey()
    {
        var id = Guid.NewGuid();

        HeatmapOverlayOrigins.IsSameSite(id.ToString(), id, "tienda").Should().BeTrue();
        HeatmapOverlayOrigins.IsSameSite("tienda", id, "tienda").Should().BeTrue();
        HeatmapOverlayOrigins.IsSameSite(Guid.NewGuid().ToString(), id, "tienda").Should().BeFalse();
        HeatmapOverlayOrigins.IsSameSite("otra", id, "tienda").Should().BeFalse();
        HeatmapOverlayOrigins.IsSameSite(null, id, "tienda").Should().BeFalse();
    }
}
