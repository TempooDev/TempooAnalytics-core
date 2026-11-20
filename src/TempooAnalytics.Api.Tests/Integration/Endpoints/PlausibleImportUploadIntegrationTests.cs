using TempooAnalytics.Api.Features.Plans;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Features.Imports.Models;
using TempooAnalytics.Api.Features.Imports.Services;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Services;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

public class PlausibleImportUploadIntegrationTests : FunctionalTestBase
{
    private readonly Mock<ISiteAccessService> _siteAccess = new();
    private readonly Mock<IUserPlanStore> _users = new();
    private readonly Mock<IStorageService> _storage = new();
    private readonly Mock<IImportService> _imports = new();
    private readonly Guid _userId = Guid.NewGuid();

    public PlausibleImportUploadIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _siteAccess.Setup(s => s.CanAccessAsync(It.IsAny<UserId>(), It.IsAny<SiteId>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _storage.Setup(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>())).ReturnsAsync("ok");
        _imports.Setup(s => s.EnqueueCsvImport(It.IsAny<SiteId>(), It.IsAny<string>(), ImportType.Plausible, It.IsAny<UserId>())).ReturnsAsync(Guid.NewGuid());
    }

    private HttpClient CreateClient(bool flagEnabled)
    {
        var client = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, conf) => conf.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:PlausibleImport"] = flagEnabled ? "true" : "false"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => _siteAccess.Object);
                services.AddScoped(_ => _users.Object);
                services.AddScoped(_ => _storage.Object);
                services.AddScoped(_ => _imports.Object);
            });
        }).CreateClient();
        AuthenticateClient(client, _userId, false);
        return client;
    }

    private void UserOnPlan(string? slug) =>
        _users.Setup(u => u.GetPlanAsync(It.IsAny<UserId>()))
            .ReturnsAsync(slug == null ? null : new UserPlan(slug, 3));

    private static MultipartFormDataContent Upload(string fileName, int size = 128)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[size]), "file", fileName);
        return content;
    }

    [Fact]
    public async Task Upload_WhenFeatureFlagIsOff_ReturnsNotFound()
    {
        UserOnPlan("pro");
        var client = CreateClient(flagEnabled: false);

        var response = await client.PostAsync($"/api/sites/{Guid.NewGuid()}/imports/plausible", Upload("export.zip"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _storage.Verify(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("free")]
    [InlineData(null)] // plan slug without a matching tier
    public async Task Upload_OnFreeOrUnknownPlan_ReturnsForbidden_AndUploadsNothing(string? plan)
    {
        UserOnPlan(plan);
        var client = CreateClient(flagEnabled: true);

        var response = await client.PostAsync($"/api/sites/{Guid.NewGuid()}/imports/plausible", Upload("export.zip"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("paid plans");
        _storage.Verify(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()), Times.Never);
        _imports.Verify(s => s.EnqueueCsvImport(It.IsAny<SiteId>(), It.IsAny<string>(), It.IsAny<ImportType>(), It.IsAny<UserId>()), Times.Never);
    }

    [Theory]
    [InlineData("export.zip", ".zip", "application/zip")]
    [InlineData("daily.CSV", ".csv", "text/csv")]
    public async Task Upload_OnPaidPlan_StoresFileAndReturnsAccepted(string fileName, string extension, string contentType)
    {
        UserOnPlan("pro");
        var siteId = Guid.NewGuid();
        var client = CreateClient(flagEnabled: true);

        var response = await client.PostAsync($"/api/sites/{siteId}/imports/plausible", Upload(fileName));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _storage.Verify(s => s.UploadFileAsync(
            It.Is<string>(k => k.StartsWith($"imports/{siteId}/") && k.EndsWith(extension)),
            It.IsAny<Stream>(),
            contentType), Times.Once);
        _imports.Verify(s => s.EnqueueCsvImport(new SiteId(siteId), It.IsAny<string>(), ImportType.Plausible, new UserId(_userId)), Times.Once);
    }

    [Fact]
    public async Task Upload_WithUnsupportedExtension_ReturnsBadRequest()
    {
        UserOnPlan("pro");
        var client = CreateClient(flagEnabled: true);

        var response = await client.PostAsync($"/api/sites/{Guid.NewGuid()}/imports/plausible", Upload("export.txt"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _storage.Verify(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FeatureFlags_AreAvailableWithoutAuthentication(bool flagEnabled)
    {
        var client = CreateClient(flagEnabled);
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.GetAsync("/api/features");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("plausibleImport").GetBoolean().Should().Be(flagEnabled);
    }

    [Fact]
    public async Task ListImports_IncludesStartAndCompletionTimes()
    {
        var ownerId = Guid.NewGuid();
        var siteId = SiteId.New();
        var startedAt = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        using (var context = Fixture.CreateContext())
        {
            context.AnalyticsSites.Add(new AnalyticsSite
            {
                Id = siteId,
                SiteKey = "imports-times",
                Name = "imports-times",
                OwnerId = new UserId(ownerId),
                CreatedAt = DateTime.UtcNow
            });
            context.ImportJobs.Add(new ImportJob
            {
                SiteId = siteId,
                Type = ImportType.Plausible,
                Status = ImportStatus.Completed,
                StartedAt = startedAt,
                CompletedAt = startedAt.AddMinutes(2)
            });
            await context.SaveChangesAsync();
        }

        AuthenticateClient(Client, ownerId, false);
        var response = await Client.GetAsync($"/api/sites/{siteId.Value}/imports");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var job = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single();
        job.GetProperty("startedAt").GetDateTime().Should().Be(startedAt);
        job.GetProperty("completedAt").GetDateTime().Should().Be(startedAt.AddMinutes(2));
    }
}
