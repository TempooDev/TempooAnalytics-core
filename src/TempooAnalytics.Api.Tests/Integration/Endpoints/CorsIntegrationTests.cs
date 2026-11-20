using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AwesomeAssertions;
using TempooAnalytics.Api.Tests.Shared;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration;

public class CorsIntegrationTests : FunctionalTestBase
{
    public CorsIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Analytics_Events_Endpoint_Returns_Correct_Cors_Headers_On_Preflight()
    {
        // Act: OPTIONS request (Preflight)
        var request = new HttpRequestMessage(HttpMethod.Options, "/analytics/events");
        request.Headers.Add("Origin", "https://any-external-site.eu");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "Content-Type, X-API-Key");

        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        // Open to any site (the tracker) but never credentialed.
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Contain("*");
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }
}
