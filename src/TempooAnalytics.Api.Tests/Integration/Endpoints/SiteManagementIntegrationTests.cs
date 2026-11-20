using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Tests.Shared;
using Xunit;
using AwesomeAssertions;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

public partial class SiteManagementIntegrationTests : FunctionalTestBase
{
    public SiteManagementIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task CreateSite_ReturnsOk_ForNormalUser()
    {
        var ownerId = Guid.NewGuid();
        AuthenticateClient(Client, ownerId, false);
        var request = new CreateSiteRequest("Test Site", "test-site", new[] { "localhost" });
        var response = await Client.PostAsJsonAsync("/api/admin/sites", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "any signed-in user creates their own sites");

        using var context = Fixture.CreateContext();
        context.AnalyticsSites.Single(s => s.SiteKey == "test-site").OwnerId.Should().Be(new UserId(ownerId));
    }

    [Fact]
    public async Task CreateSite_ReturnsUnauthorized_WithoutToken()
    {
        var response = await Client.PostAsJsonAsync("/api/admin/sites", new CreateSiteRequest("Anon", "anon-site", null));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateSite_DeletedSitesDontCountTowardsLimit()
    {
        var ownerId = Guid.NewGuid();
        await AddOwnedSiteAsync(ownerId, "deleted-first", isDeleted: true);

        AuthenticateClient(Client, ownerId, false);
        var response = await Client.PostAsJsonAsync("/api/admin/sites", new CreateSiteRequest("Replacement", "deleted-replacement", null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task AddOwnedSiteAsync(Guid ownerId, string siteKey, bool isDeleted)
    {
        using var context = Fixture.CreateContext();
        context.AnalyticsSites.Add(new AnalyticsSite
        {
            Id = SiteId.New(),
            SiteKey = siteKey,
            Name = siteKey,
            OwnerId = new UserId(ownerId),
            CreatedAt = DateTime.UtcNow,
            IsDeleted = isDeleted
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task DeleteSite_ReturnsNoContent_WhenOwner()
    {
        var ownerId = Guid.NewGuid();
        var siteKey = "delete-me";
        
        using (var context = Fixture.CreateContext())
        {
            context.AnalyticsSites.Add(new AnalyticsSite 
            { 
                Id = SiteId.New(), 
                SiteKey = siteKey, 
                Name = siteKey, 
                OwnerId = new UserId(ownerId), 
                CreatedAt = DateTime.UtcNow 
            });
            await context.SaveChangesAsync();
        }

        AuthenticateClient(Client, ownerId, false);
        var response = await Client.DeleteAsync($"/api/admin/sites/{siteKey}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
