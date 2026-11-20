using TempooAnalytics.Api.Features.Plans;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Features.Imports.Ga4;
using TempooAnalytics.Api.Features.Imports.Models;
using TempooAnalytics.Api.Features.Imports.Services;
using TempooAnalytics.Api.Features.Notifications.Models;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Services;
using TempooAnalytics.Api.Tests.Unit.Imports;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

// Heavy tier: slower integration tests, run by the full CI job rather than the quick one.
[Trait("Category", "Heavy")]
public class Ga4ImportIntegrationTests : FunctionalTestBase
{
    private const string RedirectUri = "http://localhost:4200/account/imports/google/callback";

    private readonly Mock<ISiteAccessService> _siteAccess = new();
    private readonly Mock<IUserPlanStore> _users = new();
    private readonly Mock<IStorageService> _storage = new();
    private readonly Mock<IImportQueue> _queue = new();
    private readonly Mock<IClickHouseRepository> _clickHouse = new();
    private readonly FakeGoogleHandler _google = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2024, 3, 1, 10, 0, 0, TimeSpan.Zero));
    private readonly CapturingLoggerProvider _logs = new();
    private readonly List<ImportTaskPayload> _queued = [];
    private readonly Dictionary<string, byte[]> _uploads = new();

    private readonly Guid _userId = Guid.NewGuid();
    private Guid _siteId;
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _app = null!;

    public Ga4ImportIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _siteAccess.Setup(s => s.CanAccessAsync(It.IsAny<UserId>(), It.IsAny<SiteId>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        UserOnPlan("pro");
        _storage.Setup(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .Callback<string, Stream, string>((key, stream, _) =>
            {
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                _uploads[key] = copy.ToArray();
            })
            .ReturnsAsync("ok");
        _queue.Setup(q => q.EnqueueAsync(It.IsAny<ImportTaskPayload>()))
            .Callback<ImportTaskPayload>(_queued.Add)
            .Returns(Task.CompletedTask);
        // First live event on 2024-02-01: GA4 is imported up to 2024-01-31.
        _clickHouse.Setup(c => c.GetFirstLiveEventDateAsync(It.IsAny<SiteId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DateOnly(2024, 2, 1));
    }

    private void UserOnPlan(string? slug) =>
        _users.Setup(u => u.GetPlanAsync(It.IsAny<UserId>()))
            .ReturnsAsync(slug == null ? null : new UserPlan(slug, 3));

    private async Task<HttpClient> CreateClient(bool flagEnabled = true, bool configured = true, bool notifications = false)
    {
        _siteId = await SeedSite();
        _app = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, conf) => conf.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:Ga4Import"] = flagEnabled ? "true" : "false",
                ["FeatureFlags:Notifications"] = notifications ? "true" : "false",
                ["Google:ClientId"] = configured ? "test-client-id.apps.googleusercontent.com" : null,
                ["Google:ClientSecret"] = configured ? "test-client-secret" : null,
                ["Google:RedirectUri"] = configured ? RedirectUri : null
            }));
            builder.ConfigureLogging(logging => logging.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => _siteAccess.Object);
                services.AddScoped(_ => _users.Object);
                services.AddScoped(_ => _storage.Object);
                services.AddScoped(_ => _queue.Object);
                services.AddScoped(_ => _clickHouse.Object);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_time);
                // The real Google client (and its HTTP logging), talking to a fake Google.
                services.AddHttpClient<IGoogleAnalyticsClient, GoogleAnalyticsClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _google);
            });
        });
        var client = _app.CreateClient();
        AuthenticateClient(client, _userId, false);
        return client;
    }

    private async Task<Guid> SeedSite()
    {
        var siteId = SiteId.New();
        using var context = Fixture.CreateContext();
        // Jobs store who started them (a foreign key to the user).
        if (!await context.Users.AnyAsync(u => u.Id == new UserId(_userId)))
        {
            context.Users.Add(new User
            {
                Id = new UserId(_userId),
                Username = $"ga4-{_userId:N}",
                Email = $"ga4-{_userId:N}@example.com",
                PasswordHash = "hash",
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        }
        context.AnalyticsSites.Add(new AnalyticsSite
        {
            Id = siteId,
            SiteKey = $"ga4-{siteId.Value:N}",
            Name = "ga4",
            OwnerId = new UserId(_userId),
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        return siteId.Value;
    }

    private string Url(string path = "") => $"/api/sites/{_siteId}/imports/ga4{path}";

    private async Task<(string State, Uri Url)> Authorize(HttpClient client)
    {
        var response = await client.PostAsync(Url("/authorize"), null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var url = new Uri((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("authorizationUrl").GetString()!);
        return (HttpUtility.ParseQueryString(url.Query)["state"]!, url);
    }

    private async Task<JsonElement> Connect(HttpClient client, string state, string code = "4/google-auth-code")
    {
        var response = await client.PostAsJsonAsync(Url("/connect"), new { code, state });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid> StartImport(HttpClient client, string propertyId = "properties/123")
    {
        var (state, _) = await Authorize(client);
        var connection = await Connect(client, state);
        var response = await client.PostAsJsonAsync(Url(), new { connectionId = connection.GetProperty("connectionId").GetString(), propertyId });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();
    }

    /// <summary>Runs the queued download the way the background service does.</summary>
    private async Task RunQueuedDownload()
    {
        _app.Services.GetRequiredService<Ga4DownloadQueue>().Reader.TryRead(out var request).Should().BeTrue();
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Ga4DownloadProcessor>().ProcessAsync(request!, CancellationToken.None);
    }

    private async Task<ImportJob> Job(Guid jobId)
    {
        using var context = Fixture.CreateContext();
        return await context.ImportJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
    }

    // --- 3.1 flag ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FeatureFlags_ReportGa4Import(bool flagEnabled)
    {
        var client = await CreateClient(flagEnabled);

        var body = await (await client.GetAsync("/api/features")).Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("ga4Import").GetBoolean().Should().Be(flagEnabled);
    }

    // --- 3.3 authorize ---

    [Theory]
    [InlineData("/authorize")]
    [InlineData("/connect")]
    [InlineData("")]
    public async Task Endpoints_WithFlagOff_ReturnNotFound(string path)
    {
        var client = await CreateClient(flagEnabled: false);

        var response = await client.PostAsJsonAsync(Url(path), new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Authorize_WithoutSiteAccess_ReturnsForbidden()
    {
        _siteAccess.Setup(s => s.CanAccessAsync(It.IsAny<UserId>(), It.IsAny<SiteId>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var client = await CreateClient();

        (await client.PostAsync(Url("/authorize"), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authorize_OnFreePlan_ReturnsForbiddenWithUpgradeMessage()
    {
        UserOnPlan("free");
        var client = await CreateClient();

        var response = await client.PostAsync(Url("/authorize"), null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("paid plans");
    }

    [Fact]
    public async Task Authorize_WithActiveImport_ReturnsConflict()
    {
        var client = await CreateClient();
        using (var context = Fixture.CreateContext())
        {
            context.ImportJobs.Add(new ImportJob { SiteId = new SiteId(_siteId), Type = ImportType.Plausible, Status = ImportStatus.Processing });
            await context.SaveChangesAsync();
        }

        (await client.PostAsync(Url("/authorize"), null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Authorize_WithoutGoogleConfiguration_ReturnsServiceUnavailable()
    {
        var client = await CreateClient(configured: false);

        (await client.PostAsync(Url("/authorize"), null)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Authorize_ReturnsReadOnlyOnlineConsentUrlWithPkce()
    {
        var client = await CreateClient();

        var (state, url) = await Authorize(client);

        url.GetLeftPart(UriPartial.Path).Should().Be("https://accounts.google.com/o/oauth2/v2/auth");
        var query = HttpUtility.ParseQueryString(url.Query);
        query["scope"].Should().Be("https://www.googleapis.com/auth/analytics.readonly");
        query["access_type"].Should().Be("online");
        query["response_type"].Should().Be("code");
        query["redirect_uri"].Should().Be(RedirectUri);
        query["client_id"].Should().Be("test-client-id.apps.googleusercontent.com");
        query["code_challenge_method"].Should().Be("S256");
        query["code_challenge"].Should().HaveLength(43);
        state.Should().HaveLength(43);
    }

    // --- 3.4 connect ---

    [Fact]
    public async Task Connect_WithValidState_ExchangesTheCodeWithPkce_AndListsProperties()
    {
        var client = await CreateClient();
        var (state, url) = await Authorize(client);

        var body = await Connect(client, state);

        body.GetProperty("connectionId").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("properties").EnumerateArray().Select(p => p.GetProperty("id").GetString())
            .Should().Equal("properties/123", "properties/456");
        body.GetProperty("properties")[0].GetProperty("name").GetString().Should().Be("acme.com");
        body.GetRawText().Should().NotContain(FakeGoogleHandler.AccessToken);

        var exchange = _google.Requests.Single(r => r.Url == GoogleAnalyticsClient.TokenEndpoint);
        var form = HttpUtility.ParseQueryString(exchange.Body);
        form["code"].Should().Be("4/google-auth-code");
        form["grant_type"].Should().Be("authorization_code");
        Ga4ImportEndpoints.CodeChallenge(form["code_verifier"]!).Should().Be(HttpUtility.ParseQueryString(url.Query)["code_challenge"]);
    }

    [Fact]
    public async Task Connect_WithUnknownState_ReturnsBadRequest()
    {
        var client = await CreateClient();

        var response = await client.PostAsJsonAsync(Url("/connect"), new { code = "c", state = "unknown" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _google.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Connect_WithReusedState_ReturnsBadRequest()
    {
        var client = await CreateClient();
        var (state, _) = await Authorize(client);
        await Connect(client, state);

        (await client.PostAsJsonAsync(Url("/connect"), new { code = "c", state })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Connect_WithExpiredState_ReturnsBadRequest()
    {
        var client = await CreateClient();
        var (state, _) = await Authorize(client);
        _time.Advance(TimeSpan.FromMinutes(11));

        (await client.PostAsJsonAsync(Url("/connect"), new { code = "c", state })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Connect_WithStateOfAnotherSite_ReturnsBadRequest()
    {
        var client = await CreateClient();
        var (state, _) = await Authorize(client);
        var otherSite = await SeedSite();

        var response = await client.PostAsJsonAsync($"/api/sites/{otherSite}/imports/ga4/connect", new { code = "c", state });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Connect_WhenGoogleRejects_ReturnsBadRequestWithMessage()
    {
        var client = await CreateClient();
        var (state, _) = await Authorize(client);
        _google.GrantedScope = "openid";

        var response = await client.PostAsJsonAsync(Url("/connect"), new { code = "c", state });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Contain("read access");
    }

    [Fact]
    public async Task Connect_WithoutProperties_ReturnsEmptyList()
    {
        var client = await CreateClient();
        var (state, _) = await Authorize(client);
        _google.NoProperties = true;

        var body = await Connect(client, state);

        body.GetProperty("properties").GetArrayLength().Should().Be(0);
    }

    // --- 3.5 import ---

    [Fact]
    public async Task Import_WithListedProperty_CreatesPendingJobWithoutToken()
    {
        var client = await CreateClient();

        var jobId = await StartImport(client);

        var job = await Job(jobId);
        job.Type.Should().Be(ImportType.GoogleAnalytics4);
        job.Status.Should().Be(ImportStatus.Pending);
        job.ExternalPropertyId.Should().Be("properties/123");
        job.CreatedByUserId.Should().Be(new UserId(_userId));
        job.ConfigurationJson.Should().BeNull();
        JsonSerializer.Serialize(job).Should().NotContain(FakeGoogleHandler.AccessToken);
        _queued.Should().BeEmpty("the worker only gets the job once the file is stored");
    }

    [Fact]
    public async Task Import_WithPropertyNotListed_ReturnsBadRequest_AndCreatesNoJob()
    {
        var client = await CreateClient();
        var (state, _) = await Authorize(client);
        var connection = await Connect(client, state);

        var response = await client.PostAsJsonAsync(Url(), new { connectionId = connection.GetProperty("connectionId").GetString(), propertyId = "properties/999" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var context = Fixture.CreateContext();
        (await context.ImportJobs.AnyAsync(j => j.SiteId == new SiteId(_siteId))).Should().BeFalse();
    }

    [Fact]
    public async Task Import_AfterTenMinutes_ReturnsBadRequestAskingToReconnect()
    {
        var client = await CreateClient();
        var (state, _) = await Authorize(client);
        var connection = await Connect(client, state);
        _time.Advance(TimeSpan.FromMinutes(11));

        var response = await client.PostAsJsonAsync(Url(), new { connectionId = connection.GetProperty("connectionId").GetString(), propertyId = "properties/123" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Connect with Google again");
    }

    // --- 4.3 download ---

    [Fact]
    public async Task Download_StoresTheNormalisedExport_AndQueuesTheJobWithoutToken()
    {
        var client = await CreateClient();
        _google.PropertyCreated = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        _google.DataDays = [new DateOnly(2024, 1, 20), new DateOnly(2024, 2, 3)]; // 2024-02-03 is after the cutoff
        var jobId = await StartImport(client);

        await RunQueuedDownload();

        var key = $"imports/{_siteId}/{jobId}.ga4.zip";
        _uploads.Keys.Should().Equal(key);
        using (var zip = new ZipArchive(new MemoryStream(_uploads[key])))
        {
            var visitors = new StreamReader(zip.GetEntry("imported_visitors_20240115_20240131.csv")!.Open()).ReadToEnd();
            visitors.Should().Be("date,visitors,pageviews,bounces,visits,visit_duration\n2024-01-20,120,400,60,150,9000\n");
        }

        var payload = _queued.Single();
        payload.Type.Should().Be("GA4");
        payload.JobId.Should().Be(jobId.ToString());
        payload.FilePath.Should().Be(key);
        payload.PropertyId.Should().Be("properties/123");
        (payload.Start, payload.End).Should().Be(("2024-01-15", "2024-01-31"));
        JsonSerializer.Serialize(payload, AppJsonContext.Default.ImportTaskPayload).Should().NotContainEquivalentOf("token");

        var job = await Job(jobId);
        job.Status.Should().Be(ImportStatus.Pending);
        job.StartDate.Should().Be(new DateTime(2024, 1, 15));
        job.EndDate.Should().Be(new DateTime(2024, 1, 31));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "denied access")]
    [InlineData(HttpStatusCode.TooManyRequests, "quota")]
    public async Task Download_WhenGoogleFails_MarksTheJobFailed_AndUploadsNothing(HttpStatusCode status, string message)
    {
        var client = await CreateClient();
        var jobId = await StartImport(client);
        _google.DataApiStatus = status;

        await RunQueuedDownload();

        var job = await Job(jobId);
        job.Status.Should().Be(ImportStatus.Failed);
        job.ErrorMessage.Should().Contain(message);
        _uploads.Should().BeEmpty();
        _queued.Should().BeEmpty();
    }

    [Fact]
    public async Task Download_WhenGoogleRejects_NotifiesTheUserWhoStartedTheImport()
    {
        var client = await CreateClient(notifications: true);
        var jobId = await StartImport(client);
        _google.DataApiStatus = HttpStatusCode.Unauthorized;

        await RunQueuedDownload();

        var job = await Job(jobId);
        var notification = (await Notifications()).Should().ContainSingle().Subject;
        notification.Type.Should().Be(NotificationTypes.ImportFailed);
        notification.Severity.Should().Be(NotificationSeverity.Error);
        notification.SourceId.Should().Be(jobId);
        notification.SiteId.Should().Be(new SiteId(_siteId));
        notification.Body.Should().Contain("Google Analytics").And.Contain(job.ErrorMessage!);
        notification.Link.Should().Be($"/account/sites/{_siteId}/imports");
    }

    [Fact]
    public async Task Download_WhenGoogleRejects_WithNotificationsOff_StoresNoNotification()
    {
        var client = await CreateClient(notifications: false);
        await StartImport(client);
        _google.DataApiStatus = HttpStatusCode.Unauthorized;

        await RunQueuedDownload();

        (await Notifications()).Should().BeEmpty();
    }

    private async Task<List<Notification>> Notifications()
    {
        using var context = Fixture.CreateContext();
        return await context.Notifications.AsNoTracking().Where(n => n.UserId == new UserId(_userId)).ToListAsync();
    }

    [Fact]
    public async Task Download_WithoutDataBeforeTheCutoff_FailsWithNothingToImport()
    {
        var client = await CreateClient();
        _google.DataDays = [new DateOnly(2024, 2, 10)];
        var jobId = await StartImport(client);

        await RunQueuedDownload();

        var job = await Job(jobId);
        job.Status.Should().Be(ImportStatus.Failed);
        job.ErrorMessage.Should().Be(Ga4DownloadProcessor.NothingToImportMessage);
        _uploads.Should().BeEmpty();
    }

    [Fact]
    public async Task Download_WhenThePropertyIsNewerThanTheFirstEvent_FailsWithNothingToImport()
    {
        var client = await CreateClient();
        _google.PropertyCreated = new DateTimeOffset(2024, 2, 15, 0, 0, 0, TimeSpan.Zero);
        var jobId = await StartImport(client);

        await RunQueuedDownload();

        (await Job(jobId)).ErrorMessage.Should().Be(Ga4DownloadProcessor.NothingToImportMessage);
        _google.Requests.Should().NotContain(r => r.Url.Contains(":runReport"));
    }

    // --- 4.4 interrupted downloads ---

    [Fact]
    public async Task FailInterruptedGa4Jobs_FailsOnlyOldPendingGa4JobsWithoutFile()
    {
        await CreateClient();
        var now = _time.GetUtcNow().UtcDateTime;
        var site = new SiteId(_siteId);
        var stale = new ImportJob { SiteId = site, Type = ImportType.GoogleAnalytics4, Status = ImportStatus.Pending, CreatedAt = now.AddHours(-2) };
        var recent = new ImportJob { SiteId = site, Type = ImportType.GoogleAnalytics4, Status = ImportStatus.Pending, CreatedAt = now.AddMinutes(-20) };
        var queuedForWorker = new ImportJob { SiteId = site, Type = ImportType.GoogleAnalytics4, Status = ImportStatus.Pending, CreatedAt = now.AddHours(-2), StartDate = now.AddYears(-1) };
        var plausible = new ImportJob { SiteId = site, Type = ImportType.Plausible, Status = ImportStatus.Pending, CreatedAt = now.AddHours(-2) };
        using (var context = Fixture.CreateContext())
        {
            context.ImportJobs.AddRange(stale, recent, queuedForWorker, plausible);
            await context.SaveChangesAsync();
        }

        using var scope = _app.Services.CreateScope();
        var failed = await scope.ServiceProvider.GetRequiredService<IImportService>().FailInterruptedGa4Jobs(Ga4DownloadService.InterruptedAfter);

        failed.Should().Be(1);
        var staleJob = await Job(stale.Id);
        staleJob.Status.Should().Be(ImportStatus.Failed);
        staleJob.ErrorMessage.Should().Contain("Connect with Google again");
        (await Job(recent.Id)).Status.Should().Be(ImportStatus.Pending);
        (await Job(queuedForWorker.Id)).Status.Should().Be(ImportStatus.Pending);
        (await Job(plausible.Id)).Status.Should().Be(ImportStatus.Pending);
    }

    [Fact]
    public async Task FailInterruptedGa4Jobs_NotifiesTheSiteOwner_ForJobsWithoutUser()
    {
        await CreateClient(notifications: true);
        var stale = new ImportJob { SiteId = new SiteId(_siteId), Type = ImportType.GoogleAnalytics4, Status = ImportStatus.Pending, CreatedAt = _time.GetUtcNow().UtcDateTime.AddHours(-2) };
        using (var context = Fixture.CreateContext())
        {
            context.ImportJobs.Add(stale);
            await context.SaveChangesAsync();
        }

        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IImportService>().FailInterruptedGa4Jobs(Ga4DownloadService.InterruptedAfter);

        var notification = (await Notifications()).Should().ContainSingle().Subject;
        notification.Type.Should().Be(NotificationTypes.ImportFailed);
        notification.SourceId.Should().Be(stale.Id);
        notification.Body.Should().Contain(ImportService.InterruptedGa4Message);
    }

    // --- 3.6 secrets never logged ---

    [Fact]
    public async Task FullFlow_NeverLogsCodeStateVerifierOrToken()
    {
        const string code = "4/0AVG7fiQ-secret-authorization-code";
        var client = await CreateClient();
        var (state, _) = await Authorize(client);
        var connection = await Connect(client, state, code);
        var first = await client.PostAsJsonAsync(Url(), new { connectionId = connection.GetProperty("connectionId").GetString(), propertyId = "properties/123" });
        await RunQueuedDownload();
        using (var context = Fixture.CreateContext())
        {
            var firstJobId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();
            await context.ImportJobs.Where(j => j.Id == firstJobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, ImportStatus.Completed));
        }

        _google.DataApiStatus = HttpStatusCode.Unauthorized; // a failed download must not leak either
        var (state2, _) = await Authorize(client);
        var connection2 = await Connect(client, state2, code);
        (await client.PostAsJsonAsync(Url(), new { connectionId = connection2.GetProperty("connectionId").GetString(), propertyId = "properties/123" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        await RunQueuedDownload();

        var verifier = HttpUtility.ParseQueryString(_google.Requests.First(r => r.Url == GoogleAnalyticsClient.TokenEndpoint).Body)["code_verifier"]!;
        var secrets = new[] { code, state, state2, verifier, FakeGoogleHandler.AccessToken, connection.GetProperty("connectionId").GetString()! };

        _logs.Messages.Should().Contain(m => m.Contains("analyticsdata.googleapis.com"), "HTTP client logging is on, so the check is meaningful");
        foreach (var secret in secrets)
        {
            _logs.Messages.Should().NotContain(m => m.Contains(secret));
        }
    }
}
