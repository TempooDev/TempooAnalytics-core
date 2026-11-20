using System.Collections.Generic;
using System.Net;
using AwesomeAssertions;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Model;
using MaxMind.GeoIP2.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using TempooAnalytics.Api.Infrastructure;
using Xunit;

namespace TempooAnalytics.Api.Tests.Infrastructure;

public class GeolocationServiceTests
{
    private readonly Mock<IGeoIP2DatabaseReader> _mockDbReader;
    private readonly Mock<ILogger<GeolocationService>> _mockLogger;
    private readonly GeolocationService _service;

    public GeolocationServiceTests()
    {
        _mockDbReader = new Mock<IGeoIP2DatabaseReader>();
        _mockLogger = new Mock<ILogger<GeolocationService>>();
        _service = new GeolocationService(_mockDbReader.Object, _mockLogger.Object);
    }

    [Fact]
    public void GetCountryCode_Returns_Country_From_Cloudflare_Header()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["CF-IPCountry"] = "US";

        // Act
        var result = _service.GetCountryCode(context.Request);

        // Assert
        result.Should().Be("US");
        CountryResponse? response;
        _mockDbReader.Verify(x => x.TryCountry(It.IsAny<IPAddress>(), out response), Times.Never);
    }

    [Fact]
    public void GetCountryCode_Ignores_XX_From_Cloudflare_And_Falls_Back_To_IP()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["CF-IPCountry"] = "XX";
        context.Connection.RemoteIpAddress = IPAddress.Parse("1.2.3.4");

        var country = new Country(isoCode: "US", names: new Dictionary<string, string> { { "en", "United States" } });
        var countryResponse = new CountryResponse(country: country);
        _mockDbReader.Setup(x => x.TryCountry(It.IsAny<IPAddress>(), out countryResponse)).Returns(true);

        // Act
        var result = _service.GetCountryCode(context.Request);

        // Assert
        result.Should().Be("US");
        _mockDbReader.Verify(x => x.TryCountry(It.IsAny<IPAddress>(), out countryResponse), Times.Once);
    }

    [Fact]
    public void GetCountryCode_Ignores_T1_From_Cloudflare_And_Falls_Back_To_IP()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["CF-IPCountry"] = "T1";
        context.Connection.RemoteIpAddress = IPAddress.Parse("1.2.3.4");

        var country = new Country(isoCode: "CA", names: new Dictionary<string, string> { { "en", "Canada" } });
        var countryResponse = new CountryResponse(country: country);
        _mockDbReader.Setup(x => x.TryCountry(It.IsAny<IPAddress>(), out countryResponse)).Returns(true);

        // Act
        var result = _service.GetCountryCode(context.Request);

        // Assert
        result.Should().Be("CA");
        _mockDbReader.Verify(x => x.TryCountry(It.IsAny<IPAddress>(), out countryResponse), Times.Once);
    }

    [Fact]
    public void GetCountryCode_Returns_Country_From_XGeoipCountry_Header()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Geoip-Country"] = "FR";

        // Act
        var result = _service.GetCountryCode(context.Request);

        // Assert
        result.Should().Be("FR");
    }

    [Fact]
    public void GetCountryCode_Returns_Country_From_XCountry_Header()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Country"] = "DE";

        // Act
        var result = _service.GetCountryCode(context.Request);

        // Assert
        result.Should().Be("DE");
    }

    [Fact]
    public void GetCountryCode_Falls_Back_To_IP_If_No_Headers()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("8.8.8.8");

        var country = new Country(isoCode: "JP", names: new Dictionary<string, string> { { "en", "Japan" } });
        var countryResponse = new CountryResponse(country: country);
        _mockDbReader.Setup(x => x.TryCountry(It.IsAny<IPAddress>(), out countryResponse)).Returns(true);

        // Act
        var result = _service.GetCountryCode(context.Request);

        // Assert
        result.Should().Be("JP");
        _mockDbReader.Verify(x => x.TryCountry(It.IsAny<IPAddress>(), out countryResponse), Times.Once);
    }

    [Fact]
    public void GetVisitorId_Returns_Consistent_Hash_For_Same_Inputs()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("1.2.3.4");
        context.Request.Headers.UserAgent = "Mozilla/5.0 Test Agent";

        // Act
        var result1 = _service.GetVisitorId(context.Request);
        var result2 = _service.GetVisitorId(context.Request);

        // Assert
        result1.Should().NotBeNullOrEmpty();
        result1.Should().Be(result2);
    }

    [Fact]
    public void GetVisitorId_Returns_Different_Hash_For_Different_Ip()
    {
        // Arrange
        var context1 = new DefaultHttpContext();
        context1.Connection.RemoteIpAddress = IPAddress.Parse("1.2.3.4");
        context1.Request.Headers.UserAgent = "Mozilla/5.0 Test Agent";

        var context2 = new DefaultHttpContext();
        context2.Connection.RemoteIpAddress = IPAddress.Parse("5.6.7.8");
        context2.Request.Headers.UserAgent = "Mozilla/5.0 Test Agent";

        // Act
        var result1 = _service.GetVisitorId(context1.Request);
        var result2 = _service.GetVisitorId(context2.Request);

        // Assert
        result1.Should().NotBe(result2);
    }

    [Fact]
    public void GetVisitorId_Returns_Different_Hash_For_Different_UserAgent()
    {
        // Arrange
        var context1 = new DefaultHttpContext();
        context1.Connection.RemoteIpAddress = IPAddress.Parse("1.2.3.4");
        context1.Request.Headers.UserAgent = "Mozilla/5.0 Test Agent";

        var context2 = new DefaultHttpContext();
        context2.Connection.RemoteIpAddress = IPAddress.Parse("1.2.3.4");
        context2.Request.Headers.UserAgent = "Mozilla/5.0 Other Agent";

        // Act
        var result1 = _service.GetVisitorId(context1.Request);
        var result2 = _service.GetVisitorId(context2.Request);

        // Assert
        result1.Should().NotBe(result2);
    }
}
