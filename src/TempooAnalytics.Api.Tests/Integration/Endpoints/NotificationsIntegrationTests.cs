using TempooAnalytics.Api.Features.Plans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Features.Imports.Models;
using TempooAnalytics.Api.Features.Notifications;
using TempooAnalytics.Api.Features.Notifications.Models;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Email;
using TempooAnalytics.Api.Infrastructure.Services;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

// Heavy tier: slower integration tests, run by the full CI job rather than the quick one.
[Trait("Category", "Heavy")]
public class NotificationsIntegrationTests : FunctionalTestBase
{
    private readonly Mock<IEmailService> _email = new();
    private WebApplicationFactory<Program> _app = null!;

    public NotificationsIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    private HttpClient CreateClient(Guid? userId, bool flagEnabled = true, Action<IServiceCollection>? services = null)
    {
        _app = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, conf) => conf.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:Notifications"] = flagEnabled ? "true" : "false"
            }));
            builder.ConfigureTestServices(s =>
            {
                s.AddScoped(_ => _email.Object);
                services?.Invoke(s);
            });
        });
        var client = _app.CreateClient();
        if (userId != null) AuthenticateClient(client, userId.Value, false);
        return client;
    }

    private async Task<User> SeedUser()
    {
        var user = new User
        {
            Id = UserId.New(),
            Username = $"n-{Guid.NewGuid():N}",
            Email = $"n-{Guid.NewGuid():N}@example.com",
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        using var context = Fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private async Task<AnalyticsSite> SeedSite(UserId? owner, string name = "example.com")
    {
        var site = new AnalyticsSite { Id = SiteId.New(), SiteKey = $"n-{Guid.NewGuid():N}", Name = name, OwnerId = owner, CreatedAt = DateTime.UtcNow };
        using var context = Fixture.CreateContext();
        context.AnalyticsSites.Add(site);
        await context.SaveChangesAsync();
        return site;
    }

    private async Task SeedNotifications(UserId user, int count, int read = 0, DateTime? start = null)
    {
        var first = start ?? DateTime.UtcNow.AddHours(-1);
        using var context = Fixture.CreateContext();
        for (var i = 0; i < count; i++)
        {
            context.Notifications.Add(new Notification
            {
                UserId = user,
                Type = NotificationTypes.ImportCompleted,
                Severity = NotificationSeverity.Success,
                Title = $"n{i}",
                SourceId = Guid.NewGuid(),
                CreatedAt = first.AddSeconds(i),
                ReadAt = i < read ? first : null
            });
        }
        await context.SaveChangesAsync();
    }

    private async Task<List<Notification>> NotificationsOf(UserId user)
    {
        using var context = Fixture.CreateContext();
        return await context.Notifications.AsNoTracking().Where(n => n.UserId == user).OrderByDescending(n => n.CreatedAt).ToListAsync();
    }

    private static async Task<int> UnreadCount(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/notifications/unread-count")).GetProperty("count").GetInt32();

    // --- access ---

    [Theory]
    [InlineData("GET", "/api/notifications")]
    [InlineData("GET", "/api/notifications/unread-count")]
    [InlineData("POST", "/api/notifications/read-all")]
    public async Task Endpoints_WithoutToken_ReturnUnauthorized(string method, string url)
    {
        var client = CreateClient(userId: null);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("GET", "/api/notifications")]
    [InlineData("GET", "/api/notifications/unread-count")]
    [InlineData("POST", "/api/notifications/read-all")]
    public async Task Endpoints_WithFlagOff_ReturnNotFound(string method, string url)
    {
        var user = await SeedUser();
        var client = CreateClient(user.Id.Value, flagEnabled: false);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FeatureFlags_ReportNotifications(bool flagEnabled)
    {
        var client = CreateClient(userId: null, flagEnabled);

        var body = await client.GetFromJsonAsync<JsonElement>("/api/features");

        body.GetProperty("notifications").GetBoolean().Should().Be(flagEnabled);
    }

    [Fact]
    public async Task MarkRead_NotificationOfAnotherUser_ReturnsNotFound_AndLeavesItUnread()
    {
        var owner = await SeedUser();
        var other = await SeedUser();
        await SeedNotifications(owner.Id, 1);
        var id = (await NotificationsOf(owner.Id)).Single().Id;

        var response = await CreateClient(other.Id.Value).PostAsync($"/api/notifications/{id}/read", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await NotificationsOf(owner.Id)).Single().ReadAt.Should().BeNull();
    }

    // --- list and count ---

    [Fact]
    public async Task List_PagesNewestFirst_WithCursor()
    {
        var user = await SeedUser();
        await SeedNotifications(user.Id, 30);
        await SeedNotifications((await SeedUser()).Id, 5); // someone else's
        var client = CreateClient(user.Id.Value);

        var first = await client.GetFromJsonAsync<JsonElement>("/api/notifications");
        var firstItems = first.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()).ToList();
        firstItems.Should().HaveCount(20);
        firstItems.First().Should().Be("n29");
        var cursor = first.GetProperty("nextCursor").GetString();
        cursor.Should().NotBeNullOrEmpty();

        var second = await client.GetFromJsonAsync<JsonElement>($"/api/notifications?cursor={cursor}");
        var secondItems = second.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()).ToList();
        secondItems.Should().Equal(Enumerable.Range(0, 10).Reverse().Select(i => $"n{i}"));
        second.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task List_WithLimit_IsCappedAt50()
    {
        var user = await SeedUser();
        await SeedNotifications(user.Id, 55);
        var client = CreateClient(user.Id.Value);

        var page = await client.GetFromJsonAsync<JsonElement>("/api/notifications?limit=500");

        page.GetProperty("items").GetArrayLength().Should().Be(50);
    }

    [Fact]
    public async Task List_ReturnsTheFieldsOfTheContract()
    {
        var user = await SeedUser();
        await SeedNotifications(user.Id, 1);
        var client = CreateClient(user.Id.Value);

        var item = (await client.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("items")[0];

        item.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "id", "type", "severity", "title", "body", "link", "siteId", "createdAt", "readAt");
        item.GetProperty("severity").GetInt32().Should().Be((int)NotificationSeverity.Success);
    }

    [Fact]
    public async Task List_WithInvalidCursor_ReturnsBadRequest()
    {
        var user = await SeedUser();
        var client = CreateClient(user.Id.Value);

        var response = await client.GetAsync("/api/notifications?cursor=not-a-cursor");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnreadCount_CountsOnlyUnread()
    {
        var user = await SeedUser();
        await SeedNotifications(user.Id, 8, read: 5);
        var client = CreateClient(user.Id.Value);

        (await UnreadCount(client)).Should().Be(3);
    }

    // --- mark as read ---

    [Fact]
    public async Task MarkRead_DecrementsTheCount_AndKeepsTheFirstReadDate()
    {
        var user = await SeedUser();
        await SeedNotifications(user.Id, 2);
        var client = CreateClient(user.Id.Value);
        var id = (await NotificationsOf(user.Id)).First().Id;

        var response = await client.PostAsync($"/api/notifications/{id}/read", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await UnreadCount(client)).Should().Be(1);
        var readAt = (await NotificationsOf(user.Id)).Single(n => n.Id == id).ReadAt;
        readAt.Should().NotBeNull();

        await Task.Delay(20);
        (await client.PostAsync($"/api/notifications/{id}/read", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await NotificationsOf(user.Id)).Single(n => n.Id == id).ReadAt.Should().Be(readAt);
    }

    [Fact]
    public async Task ReadAll_LeavesNothingUnread_OnlyForTheUser()
    {
        var user = await SeedUser();
        var other = await SeedUser();
        await SeedNotifications(user.Id, 4, read: 1);
        await SeedNotifications(other.Id, 2);
        var client = CreateClient(user.Id.Value);

        var response = await client.PostAsync("/api/notifications/read-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await UnreadCount(client)).Should().Be(0);
        (await NotificationsOf(other.Id)).Should().OnlyContain(n => n.ReadAt == null);
    }

    // --- creation ---

    [Fact]
    public async Task Dispatch_SameSourceTwice_StoresOne()
    {
        var user = await SeedUser();
        CreateClient(user.Id.Value);
        var notification = new UserNotification(user.Id, NotificationTypes.ImportCompleted, NotificationSeverity.Success, "Import completed", SourceId: Guid.NewGuid());

        using (var scope = _app.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            await Task.WhenAll(service.DispatchAsync(notification), _app.Services.CreateScope().ServiceProvider.GetRequiredService<INotificationDispatcher>().DispatchAsync(notification));
            await service.DispatchAsync(notification);
        }

        (await NotificationsOf(user.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task Dispatch_WithFlagOff_StoresNothing()
    {
        var user = await SeedUser();
        CreateClient(user.Id.Value, flagEnabled: false);

        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<INotificationDispatcher>()
            .DispatchAsync(new UserNotification(user.Id, NotificationTypes.ImportCompleted, NotificationSeverity.Success, "x", SourceId: Guid.NewGuid()));

        (await NotificationsOf(user.Id)).Should().BeEmpty();
    }

    // --- queue:notifications ---

    private async Task Receive(string json)
    {
        var listener = new JobNotificationListener(_app.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<JobNotificationListener>.Instance);
        await listener.ProcessNotificationAsync(json, CancellationToken.None);
    }

    private async Task<ImportJob> SeedImport(SiteId site, UserId? createdBy, ImportType type = ImportType.Plausible)
    {
        var job = new ImportJob { SiteId = site, Type = type, Status = ImportStatus.Completed, CreatedByUserId = createdBy, RowsProcessed = 42 };
        using var context = Fixture.CreateContext();
        context.ImportJobs.Add(job);
        await context.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task ImportCompletedMessage_NotifiesTheUserWhoStartedIt_Once()
    {
        var owner = await SeedUser();
        var member = await SeedUser();
        var site = await SeedSite(owner.Id, "shop.example");
        var job = await SeedImport(site.Id, member.Id);
        CreateClient(member.Id.Value);
        var json = $$"""{"type":"IMPORT_COMPLETED","jobId":"{{job.Id}}","siteId":"{{site.Id.Value}}","rowsProcessed":"1500"}""";

        await Receive(json);
        await Receive(json);

        var n = (await NotificationsOf(member.Id)).Should().ContainSingle().Subject;
        n.Type.Should().Be(NotificationTypes.ImportCompleted);
        n.Severity.Should().Be(NotificationSeverity.Success);
        n.Body.Should().Contain("Plausible").And.Contain("shop.example").And.Contain("1,500");
        n.Link.Should().Be($"/account/sites/{site.Id.Value}/imports");
        n.SiteId.Should().Be(site.Id);
        n.SourceId.Should().Be(job.Id);
        (await NotificationsOf(owner.Id)).Should().BeEmpty();
        _email.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ImportFailedMessage_ForJobWithoutUser_NotifiesTheSiteOwner()
    {
        var owner = await SeedUser();
        var site = await SeedSite(owner.Id);
        var job = await SeedImport(site.Id, createdBy: null, ImportType.GoogleAnalytics4);
        CreateClient(owner.Id.Value);

        await Receive($$"""{"type":"IMPORT_FAILED","jobId":"{{job.Id}}","siteId":"{{site.Id.Value}}","errorMessage":"invalid file"}""");

        var n = (await NotificationsOf(owner.Id)).Should().ContainSingle().Subject;
        n.Type.Should().Be(NotificationTypes.ImportFailed);
        n.Severity.Should().Be(NotificationSeverity.Error);
        n.Body.Should().Contain("Google Analytics").And.Contain("invalid file");
    }

    [Fact]
    public async Task ExportMessages_NotifyTheRequester_AndOnlyCompletedSendsTheEmail()
    {
        var user = await SeedUser();
        var site = await SeedSite(user.Id);
        var done = new ExportJob { SiteId = site.Id, UserId = user.Id, Status = ExportStatus.Completed };
        var failed = new ExportJob { SiteId = site.Id, UserId = user.Id, Status = ExportStatus.Failed };
        using (var context = Fixture.CreateContext())
        {
            context.ExportJobs.AddRange(done, failed);
            await context.SaveChangesAsync();
        }
        CreateClient(user.Id.Value);

        await Receive($$"""{"type":"EXPORT_COMPLETED","jobId":"{{done.Id}}","siteId":"{{site.Id.Value}}","email":"{{user.Email}}","downloadUrl":"https://r2.example/x.csv"}""");
        await Receive($$"""{"type":"EXPORT_FAILED","jobId":"{{failed.Id}}","siteId":"{{site.Id.Value}}","errorMessage":"upload failed"}""");

        var notifications = await NotificationsOf(user.Id);
        notifications.Should().HaveCount(2);
        var ready = notifications.Single(n => n.Type == NotificationTypes.ExportCompleted);
        ready.Link.Should().Be("/");
        ready.Body.Should().NotContain("r2.example");
        notifications.Single(n => n.Type == NotificationTypes.ExportFailed).Body.Should().NotContain("upload failed");
        _email.Verify(e => e.SendExportReadyEmailAsync(user.Email!, "https://r2.example/x.csv"), Times.Once);
        _email.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExportCompletedMessage_WithFlagOff_StillSendsTheEmail_ButStoresNoNotification()
    {
        var user = await SeedUser();
        var site = await SeedSite(user.Id);
        var done = new ExportJob { SiteId = site.Id, UserId = user.Id, Status = ExportStatus.Completed };
        using (var context = Fixture.CreateContext())
        {
            context.ExportJobs.Add(done);
            await context.SaveChangesAsync();
        }
        CreateClient(user.Id.Value, flagEnabled: false);

        await Receive($$"""{"type":"EXPORT_COMPLETED","jobId":"{{done.Id}}","siteId":"{{site.Id.Value}}","email":"{{user.Email}}","downloadUrl":"https://r2.example/x.csv"}""");

        _email.Verify(e => e.SendExportReadyEmailAsync(user.Email!, "https://r2.example/x.csv"), Times.Once);
        (await NotificationsOf(user.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task ExportCompletedMessage_ForADeletedSite_StillSendsTheEmail()
    {
        var user = await SeedUser();
        var site = await SeedSite(user.Id);
        var done = new ExportJob { SiteId = site.Id, UserId = user.Id, Status = ExportStatus.Completed };
        using (var context = Fixture.CreateContext())
        {
            context.ExportJobs.Add(done);
            await context.SaveChangesAsync();
            await context.AnalyticsSites.IgnoreQueryFilters().Where(s => s.Id == site.Id).ExecuteDeleteAsync();
        }
        CreateClient(user.Id.Value);

        await Receive($$"""{"type":"EXPORT_COMPLETED","jobId":"{{done.Id}}","siteId":"{{site.Id.Value}}","downloadUrl":"https://r2.example/x.csv"}""");

        _email.Verify(e => e.SendExportReadyEmailAsync(user.Email!, "https://r2.example/x.csv"), Times.Once);
    }

    // --- export that can't be queued ---

    [Fact]
    public async Task Export_WhenTheQueueIsUnavailable_FailsTheJob_AndNotifiesTheRequester()
    {
        var user = await SeedUser();
        var site = await SeedSite(user.Id);
        var users = new Mock<IUserRepository>();
        var plans = new Mock<IUserPlanStore>();
        plans.Setup(p => p.GetPlanAsync(It.IsAny<UserId>())).ReturnsAsync(new UserPlan("pro", 3));
        users.Setup(u => u.FindByIdAsync(user.Id)).ReturnsAsync(user);
        var access = new Mock<ISiteAccessService>();
        access.Setup(a => a.CanAccessAsync(It.IsAny<UserId>(), It.IsAny<SiteId>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        // No Redis in tests: enqueuing the export throws.
        var client = CreateClient(user.Id.Value, services: s =>
        {
            s.AddScoped(_ => users.Object);
            s.AddScoped(_ => plans.Object);
            s.AddScoped(_ => access.Object);
        });

        var response = await client.PostAsJsonAsync($"/api/sites/{site.Id.Value}/export", new { from = DateTime.UtcNow.AddDays(-7), to = DateTime.UtcNow });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        Notification? notification = null;
        for (var i = 0; i < 50 && notification == null; i++)
        {
            await Task.Delay(100);
            notification = (await NotificationsOf(user.Id)).SingleOrDefault();
        }
        notification.Should().NotBeNull();
        notification!.Type.Should().Be(NotificationTypes.ExportFailed);
        notification.Body.Should().NotContain("Failed to queue");
        using var context = Fixture.CreateContext();
        (await context.ExportJobs.SingleAsync(j => j.Id == notification.SourceId)).Status.Should().Be(ExportStatus.Failed);
    }

    // --- retention ---

    [Fact]
    public async Task Retention_DeletesNotificationsOlderThan90Days()
    {
        var user = await SeedUser();
        var now = DateTime.UtcNow;
        await SeedNotifications(user.Id, 1, start: now.AddDays(-91));
        await SeedNotifications(user.Id, 1, start: now.AddDays(-89));

        using (var context = Fixture.CreateContext())
        {
            await NotificationRetention.DeleteExpiredAsync(context, now);
        }

        (await NotificationsOf(user.Id)).Should().ContainSingle().Which.CreatedAt.Should().BeAfter(now.AddDays(-90));
    }

    // --- deletion with the account ---

    [Fact]
    public async Task DeletingTheUser_DeletesTheirNotifications()
    {
        var user = await SeedUser();
        await SeedNotifications(user.Id, 3);

        using (var context = Fixture.CreateContext())
        {
            await context.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
        }

        (await NotificationsOf(user.Id)).Should().BeEmpty();
    }
}
