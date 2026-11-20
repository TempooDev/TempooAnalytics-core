using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Infrastructure.Common;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

public class ImportExportAccessIntegrationTests : FunctionalTestBase
{
    public ImportExportAccessIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> SeedSite(Guid ownerId, string siteKey)
    {
        var siteId = SiteId.New();
        using var context = Fixture.CreateContext();
        context.AnalyticsSites.Add(new AnalyticsSite
        {
            Id = siteId,
            SiteKey = siteKey,
            Name = siteKey,
            OwnerId = new UserId(ownerId),
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        return siteId.Value;
    }

    [Fact]
    public async Task ListImports_WithoutAuthentication_ReturnsUnauthorized()
    {
        var siteId = await SeedSite(Guid.NewGuid(), "imports-anon");

        var response = await Client.GetAsync($"/api/sites/{siteId}/imports");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListImports_ForAnotherUsersSite_ReturnsForbidden()
    {
        var siteId = await SeedSite(Guid.NewGuid(), "imports-other");

        AuthenticateClient(Client, Guid.NewGuid(), false);
        var response = await Client.GetAsync($"/api/sites/{siteId}/imports");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListImports_ForOwnSite_ReturnsOk()
    {
        var ownerId = Guid.NewGuid();
        var siteId = await SeedSite(ownerId, "imports-own");

        AuthenticateClient(Client, ownerId, false);
        var response = await Client.GetAsync($"/api/sites/{siteId}/imports");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ga4Import_WithFlagOff_ReturnsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var siteId = await SeedSite(ownerId, "imports-ga4");

        AuthenticateClient(Client, ownerId, false);
        var response = await Client.PostAsJsonAsync($"/api/sites/{siteId}/imports/ga4", new
        {
            connectionId = "abc",
            propertyId = "properties/123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Export_ForAnotherUsersSite_ReturnsForbidden()
    {
        var siteId = await SeedSite(Guid.NewGuid(), "export-other");

        AuthenticateClient(Client, Guid.NewGuid(), false);
        var response = await Client.PostAsJsonAsync($"/api/sites/{siteId}/export", new
        {
            from = DateTime.UtcNow.AddDays(-7),
            to = DateTime.UtcNow
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Export_WithoutAuthentication_ReturnsUnauthorized()
    {
        var siteId = await SeedSite(Guid.NewGuid(), "export-anon");

        var response = await Client.PostAsJsonAsync($"/api/sites/{siteId}/export", new
        {
            from = DateTime.UtcNow.AddDays(-7),
            to = DateTime.UtcNow
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExportStatus_ForAnotherUsersSite_ReturnsForbidden()
    {
        var siteId = await SeedSite(Guid.NewGuid(), "export-status-other");

        AuthenticateClient(Client, Guid.NewGuid(), false);
        var response = await Client.GetAsync($"/api/sites/{siteId}/export/status");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
