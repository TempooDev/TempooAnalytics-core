using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Infrastructure.Common;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

/// <summary>
/// Deleting a user removes their site links and the user in one transaction (the core's half of <c>DeleteUserIntegrationTests</c>, which also
/// covers the subscriptions of the cloud). These run in the public tree and against the cloud composition alike.
/// </summary>
public class CoreDeleteUserIntegrationTests : FunctionalTestBase
{
    public CoreDeleteUserIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    private sealed class FailWhenAUserIsDeleted : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<User>().Any(e => e.State == EntityState.Deleted))
                throw new InvalidOperationException("removing the user failed");
            return base.SavingChangesAsync(eventData, result, ct);
        }
    }

    private async Task<UserId> SeedUserWithSiteAsync()
    {
        var user = new User { Id = UserId.New(), Username = $"u-{Guid.NewGuid():N}@example.com", Email = $"u-{Guid.NewGuid():N}@example.com", PasswordHash = "hash", IsActive = true, CreatedAt = DateTime.UtcNow };
        var site = new AnalyticsSite { Id = SiteId.New(), SiteKey = $"s-{Guid.NewGuid():N}", Name = "example.com", OwnerId = null, CreatedAt = DateTime.UtcNow };
        using var core = Fixture.CreateContext();
        core.Users.Add(user);
        core.AnalyticsSites.Add(site);
        core.UserSites.Add(new UserSite { UserId = user.Id, SiteId = site.Id });
        await core.SaveChangesAsync();
        return user.Id;
    }

    private async Task<(bool User, bool Sites)> StateAsync(UserId user)
    {
        using var core = Fixture.CreateContext();
        return (await core.Users.AnyAsync(u => u.Id == user), await core.UserSites.AnyAsync(s => s.UserId == user));
    }

    [Fact]
    public async Task Delete_RemovesTheUserAndTheirSiteLinks()
    {
        var user = await SeedUserWithSiteAsync();
        using (var scope = Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IUserRepository>().DeleteUserAsync(user);

        (await StateAsync(user)).Should().Be((false, false));
    }

    [Fact]
    public async Task Delete_OfAnUnknownUser_DoesNothing()
    {
        var other = await SeedUserWithSiteAsync();
        using (var scope = Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IUserRepository>().DeleteUserAsync(UserId.New());

        (await StateAsync(other)).Should().Be((true, true));
    }

    [Fact]
    public async Task WhenRemovingTheUserFails_TheSiteLinksAreNotLeftDeleted()
    {
        var user = await SeedUserWithSiteAsync();
        using (var core = new AnalyticsDbContext(new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseNpgsql(Fixture.ConnectionString).UseSnakeCaseNamingConvention().AddInterceptors(new FailWhenAUserIsDeleted()).Options))
        {
            var repo = new UserRepository(core, new NoPlanUserStore());

            var act = () => repo.DeleteUserAsync(user);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*removing the user failed*");
        }

        // The bulk delete of the links ran before the failure and was rolled back.
        (await StateAsync(user)).Should().Be((true, true));
    }

    [Fact]
    public async Task Delete_JoinsATransactionTheCallerAlreadyOpened()
    {
        var user = await SeedUserWithSiteAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            await repo.BeginTransactionAsync();
            await repo.DeleteUserAsync(user);
            await repo.RollbackTransactionAsync();
        }

        (await StateAsync(user)).Should().Be((true, true), "the caller's rollback undoes the whole deletion");
    }
}
