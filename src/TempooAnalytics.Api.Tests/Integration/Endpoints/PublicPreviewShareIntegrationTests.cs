using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Infrastructure.Common;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

/// <summary>
/// A share token opens the site's analytics to anyone, and site keys are public (tracking snippet),
/// so only users with access to the site may generate one.
/// </summary>
public class PublicPreviewShareIntegrationTests : FunctionalTestBase
{
    public PublicPreviewShareIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    private sealed record ShareResponse(string Token);

    [Fact]
    public async Task Share_ByOwner_ReturnsTokenStoredOnSite()
    {
        var ownerId = Guid.NewGuid();
        await AddSiteAsync(ownerId, "share-own");

        AuthenticateClient(Client, ownerId, false);
        var response = await Client.PostAsync("/api/sites/share-own/share", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ShareResponse>();
        (await StoredTokenAsync("share-own")).Should().Be(body!.Token);
    }

    [Fact]
    public async Task Share_BySiteUuid_ReturnsToken()
    {
        var ownerId = Guid.NewGuid();
        var siteId = await AddSiteAsync(ownerId, "share-uuid");

        AuthenticateClient(Client, ownerId, false);
        var response = await Client.PostAsync($"/api/sites/{siteId.Value}/share", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Share_ByAnotherUser_ReturnsNotFound_AndCreatesNoToken()
    {
        var siteId = await AddSiteAsync(Guid.NewGuid(), "share-victim");

        AuthenticateClient(Client, Guid.NewGuid(), false);
        var byKey = await Client.PostAsync("/api/sites/share-victim/share", null);
        var byUuid = await Client.PostAsync($"/api/sites/{siteId.Value}/share", null);

        byKey.StatusCode.Should().Be(HttpStatusCode.NotFound);
        byUuid.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await StoredTokenAsync("share-victim")).Should().BeNull();
    }

    [Fact]
    public async Task Share_ByUserWithGrantedAccess_ReturnsToken()
    {
        var memberId = Guid.NewGuid();
        var siteId = await AddSiteAsync(Guid.NewGuid(), "share-member");
        using (var context = Fixture.CreateContext())
        {
            context.UserSites.Add(new UserSite { UserId = new UserId(memberId), SiteId = siteId });
            await context.SaveChangesAsync();
        }

        AuthenticateClient(Client, memberId, false);
        var response = await Client.PostAsync("/api/sites/share-member/share", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<string?> StoredTokenAsync(string siteKey)
    {
        using var context = Fixture.CreateContext();
        return (await context.AnalyticsSites.SingleAsync(s => s.SiteKey == siteKey)).PublicShareToken;
    }

    private async Task<SiteId> AddSiteAsync(Guid ownerId, string siteKey)
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
        return siteId;
    }
}
