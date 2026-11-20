using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Notifications;
using TempooAnalytics.Api.Features.Notifications.Channels;
using TempooAnalytics.Api.Features.Notifications.Models;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.FeatureFlags;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Notifications;

public class InAppNotificationChannelTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly AnalyticsDbContext _db;
    private readonly Mock<IFeatureFlags> _flags = new();
    private readonly InAppNotificationChannel _service;

    public InAppNotificationChannelTests()
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase($"notifications-{Guid.NewGuid()}")
            .Options;
        _db = new InMemoryAnalyticsDbContext(options);
        _flags.Setup(f => f.Notifications).Returns(true);
        _service = new InAppNotificationChannel(_db, _flags.Object, new FixedTimeProvider(Now), NullLogger<InAppNotificationChannel>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static UserNotification Sample(UserId user, Guid? sourceId = null, string type = NotificationTypes.ImportCompleted) =>
        new(user, type, NotificationSeverity.Success, "Import completed", "Body", "/account/sites/x/imports", SiteId.New(), sourceId);

    [Fact]
    public async Task SendAsync_StoresTheNotificationUnread()
    {
        var user = UserId.New();

        await _service.SendAsync(Sample(user, Guid.NewGuid()));

        var stored = _db.Notifications.Should().ContainSingle().Subject;
        stored.UserId.Should().Be(user);
        stored.Type.Should().Be(NotificationTypes.ImportCompleted);
        stored.CreatedAt.Should().Be(Now.UtcDateTime);
        stored.ReadAt.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_WithFlagOff_StoresNothing()
    {
        _flags.Setup(f => f.Notifications).Returns(false);

        await _service.SendAsync(Sample(UserId.New(), Guid.NewGuid()));

        _db.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_SameUserTypeAndSourceTwice_StoresOne()
    {
        var user = UserId.New();
        var source = Guid.NewGuid();

        await _service.SendAsync(Sample(user, source));
        await _service.SendAsync(Sample(user, source));

        _db.Notifications.Should().ContainSingle();
    }

    [Fact]
    public async Task SendAsync_SameSourceWithAnotherType_StoresBoth()
    {
        var user = UserId.New();
        var source = Guid.NewGuid();

        await _service.SendAsync(Sample(user, source, NotificationTypes.ImportFailed));
        await _service.SendAsync(Sample(user, source, NotificationTypes.ImportCompleted));

        _db.Notifications.Should().HaveCount(2);
    }

    [Fact]
    public async Task SendAsync_TruncatesLongTexts()
    {
        var user = UserId.New();

        await _service.SendAsync(Sample(user) with { Title = new string('t', 500), Body = new string('b', 5000) });

        var stored = _db.Notifications.Single();
        stored.Title.Length.Should().Be(InAppNotificationChannel.MaxTitleLength);
        stored.Body!.Length.Should().Be(InAppNotificationChannel.MaxBodyLength);
    }

    [Fact]
    public async Task SendAsync_WhenTheDatabaseFails_DoesNotThrow()
    {
        _db.Dispose();

        var act = () => _service.SendAsync(Sample(UserId.New(), Guid.NewGuid()));

        await act.Should().NotThrowAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>The production model has JsonDocument columns (Postgres jsonb) the InMemory provider can't map.</summary>
    private sealed class InMemoryAnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : AnalyticsDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<AuditLog>().Ignore(e => e.Details);
            modelBuilder.Entity<OutboxMessage>().Ignore(e => e.Payload);
            if (modelBuilder.Model.FindEntityType(typeof(AnalyticsEvent)) != null)
            {
                modelBuilder.Entity<AnalyticsEvent>().Ignore(e => e.Data);
            }
        }
    }
}
