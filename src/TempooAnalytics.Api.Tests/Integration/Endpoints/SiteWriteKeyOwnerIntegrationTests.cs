using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Infrastructure.Common;
using Xunit;
using AwesomeAssertions;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

public class SiteWriteKeyOwnerIntegrationTests : FunctionalTestBase
{
    public SiteWriteKeyOwnerIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GenerateWriteKey_ReturnsOk_WhenIsOwner()
    {
        var ownerId = Guid.NewGuid();
        var siteKey = "owner-site";
        
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
        var response = await Client.PostAsync($"/api/sites/{siteKey}/write-key", null);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GenerateWriteKey_ReturnsForbidden_WhenNotOwner()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var siteKey = "other-site";
        
        using (var context = Fixture.CreateContext())
        {
            context.AnalyticsSites.Add(new AnalyticsSite 
            { 
                Id = SiteId.New(), 
                SiteKey = siteKey, 
                Name = siteKey, 
                OwnerId = new UserId(otherId), 
                CreatedAt = DateTime.UtcNow 
            });
            await context.SaveChangesAsync();
        }

        AuthenticateClient(Client, ownerId, false);
        var response = await Client.PostAsync($"/api/sites/{siteKey}/write-key", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
