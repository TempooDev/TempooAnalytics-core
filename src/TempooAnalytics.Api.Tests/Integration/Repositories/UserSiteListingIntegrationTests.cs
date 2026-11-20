using System;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Infrastructure.Common;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Repositories;

/// <summary>
/// The background workers (usage reporting, retention, lifecycle emails) list a user's sites through
/// IUserRepository.ListSitesForUserAsync. Sites created through the API only set OwnerId, so the listing
/// must include owned sites as well as the ones shared through analytics_user_sites.
/// </summary>
public class UserSiteListingIntegrationTests : FunctionalTestBase
{
    public UserSiteListingIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task SiteCreatedThroughApi_IsListedForItsOwner()
    {
        var ownerId = Guid.NewGuid();
        AuthenticateClient(Client, ownerId, true);
        var response = await Client.PostAsJsonAsync("/api/admin/sites", new CreateSiteRequest("Created", "created-site", null));
        response.EnsureSuccessStatusCode();
        var siteId = Fixture.CreateContext().AnalyticsSites.Single(s => s.SiteKey == "created-site").Id;

        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        (await users.ListSitesForUserAsync(new UserId(ownerId))).Should().Equal(siteId);
        (await users.GetUserSitesDetailAsync(new UserId(ownerId))).Select(s => s.SiteKey).Should().Equal("created-site");
    }

    [Fact]
    public async Task ListSitesForUser_CombinesOwnedAndSharedSites_WithoutDeletedOrDuplicates()
    {
        var userId = Guid.NewGuid();
        var owned = await SeedSiteAsync(userId, "owned");
        var ownedAndShared = await SeedSiteAsync(userId, "owned-and-shared", sharedWith: userId);
        var shared = await SeedSiteAsync(Guid.NewGuid(), "shared", sharedWith: userId);
        await SeedSiteAsync(userId, "owned-deleted", isDeleted: true);
        await SeedSiteAsync(Guid.NewGuid(), "shared-deleted", isDeleted: true, sharedWith: userId);
        await SeedSiteAsync(Guid.NewGuid(), "someone-else");

        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        (await users.ListSitesForUserAsync(new UserId(userId))).Should().BeEquivalentTo(new[] { owned, ownedAndShared, shared });
        (await users.GetUserSitesDetailAsync(new UserId(userId))).Select(s => s.SiteKey)
            .Should().BeEquivalentTo("owned", "owned-and-shared", "shared");
    }

    [Fact]
    public async Task SiteAccess_AllowsOwnerAndSharedUsers_ButNotDeletedSitesOrStrangers()
    {
        var ownerId = Guid.NewGuid();
        var sharedUserId = Guid.NewGuid();
        var site = await SeedSiteAsync(ownerId, "access-site", sharedWith: sharedUserId);
        var deleted = await SeedSiteAsync(ownerId, "access-deleted", isDeleted: true, sharedWith: sharedUserId);

        using var scope = Factory.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<ISiteAccessService>();

        (await access.CanAccessAsync(new UserId(ownerId), site)).Should().BeTrue();
        (await access.CanAccessAsync(new UserId(sharedUserId), site)).Should().BeTrue();
        (await access.CanAccessAsync(new UserId(Guid.NewGuid()), site)).Should().BeFalse();
        (await access.CanAccessAsync(new UserId(ownerId), deleted)).Should().BeFalse();
        (await access.CanAccessAsync(new UserId(sharedUserId), deleted)).Should().BeFalse();
    }

    private async Task<SiteId> SeedSiteAsync(Guid ownerId, string siteKey, bool isDeleted = false, Guid? sharedWith = null)
    {
        var siteId = SiteId.New();
        using var context = Fixture.CreateContext();
        context.AnalyticsSites.Add(new AnalyticsSite
        {
            Id = siteId,
            SiteKey = siteKey,
            Name = siteKey,
            OwnerId = new UserId(ownerId),
            CreatedAt = DateTime.UtcNow,
            IsDeleted = isDeleted
        });
        if (sharedWith is { } userId)
            context.UserSites.Add(new UserSite { UserId = new UserId(userId), SiteId = siteId });
        await context.SaveChangesAsync();
        return siteId;
    }
}
