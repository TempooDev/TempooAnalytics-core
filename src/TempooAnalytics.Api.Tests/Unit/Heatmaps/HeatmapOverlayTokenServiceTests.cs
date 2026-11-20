using System;
using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using TempooAnalytics.Api.Features.Heatmaps;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Tests.Unit.Imports;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Heatmaps;

public class HeatmapOverlayTokenServiceTests
{
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero));
    private readonly EphemeralDataProtectionProvider _keys = new();
    private readonly HeatmapOverlayTokenService _tokens;

    public HeatmapOverlayTokenServiceTests() => _tokens = new HeatmapOverlayTokenService(_keys, _time);

    [Fact]
    public void Read_ValidToken_ReturnsSiteAndPath()
    {
        var siteId = SiteId.New();
        var (token, expiresAt) = _tokens.Issue(siteId, "/precios");

        var grant = _tokens.Read(token);

        grant.Should().NotBeNull();
        grant!.SiteId.Should().Be(siteId);
        grant.Path.Should().Be("/precios");
        expiresAt.Should().Be(_time.Now.AddMinutes(10));
    }

    [Fact]
    public void Read_AfterTenMinutes_ReturnsNull()
    {
        var (token, _) = _tokens.Issue(SiteId.New(), "/precios");

        _time.Advance(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1)));

        _tokens.Read(token).Should().BeNull();
    }

    [Fact]
    public void Read_TamperedToken_ReturnsNull()
    {
        var (token, _) = _tokens.Issue(SiteId.New(), "/precios");
        var tampered = token[..^4] + (token[^4] == 'A' ? "B" : "A") + token[^3..];

        _tokens.Read(tampered).Should().BeNull();
        _tokens.Read("").Should().BeNull();
        _tokens.Read("not-a-token").Should().BeNull();
    }

    [Fact]
    public void Read_TokenFromAnotherPurpose_ReturnsNull()
    {
        var other = _keys.CreateProtector("public-preview").Protect("{\"SiteId\":\"" + Guid.NewGuid() + "\",\"Path\":\"/\",\"ExpiresAt\":9999999999}");

        _tokens.Read(other).Should().BeNull();
    }
}
