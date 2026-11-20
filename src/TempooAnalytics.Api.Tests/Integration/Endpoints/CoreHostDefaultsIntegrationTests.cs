using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Features.Onboarding.Services;
using TempooAnalytics.Api.Infrastructure.Modules;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

/// <summary>
/// What the core host answers with no module loaded (<c>Modules:Enabled=false</c>; in the public tree there are none anyway): the identity
/// of the user and nothing else on <c>/api/auth/me</c>, and the default language resolver. The core's half of
/// <c>MeProfileIntegrationTests</c> and <c>UserLanguageResolverHostTests</c> (private, they also seed subscriptions and leads).
/// </summary>
public class CoreHostDefaultsIntegrationTests : FunctionalTestBase
{
    public CoreHostDefaultsIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Me_WithoutModules_ReturnsIdentityOnly()
    {
        var userId = Guid.NewGuid();
        using (var db = Fixture.CreateContext())
        {
            db.Users.Add(new User
            {
                Id = new UserId(userId), Username = $"u-{userId:N}@example.com", Email = $"u-{userId:N}@example.com",
                PasswordHash = "hash", IsActive = true, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        using var core = Factory.WithWebHostBuilder(b => b.UseSetting(FeatureModuleLoader.EnabledSetting, "false"));
        var client = core.CreateClient();
        AuthenticateClient(client, userId, false);

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(userId);
        body.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("id", "username", "email", "isAdmin");
    }

    [Fact]
    public async Task WithoutModules_TheLanguageResolverIsTheDefault_AndAnswersSpanish()
    {
        using var core = Factory.WithWebHostBuilder(b => b.UseSetting(FeatureModuleLoader.EnabledSetting, "false"));
        using var scope = core.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IUserLanguageResolver>();

        resolver.Should().BeOfType<DefaultUserLanguageResolver>();
        (await resolver.GetLanguageAsync("anyone@example.com")).Should().Be("es");
        (await resolver.GetLanguageAsync("")).Should().Be("es");
    }
}
