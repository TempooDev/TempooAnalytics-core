using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Notifications;
using TempooAnalytics.Api.Features.Notifications.Channels;
using TempooAnalytics.Api.Features.Notifications.Models;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Infrastructure.Email;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Notifications;

public class EmailNotificationChannelTests : IDisposable
{
    private readonly AnalyticsDbContext _db;
    private readonly Mock<IEmailService> _email = new();
    private readonly EmailNotificationChannel _channel;
    private readonly User _user = new() { Id = UserId.New(), Username = "u", Email = "user@example.com", IsActive = true, CreatedAt = DateTime.UtcNow };

    public EmailNotificationChannelTests()
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase($"email-channel-{Guid.NewGuid()}")
            .Options;
        _db = new InMemoryAnalyticsDbContext(options);
        _db.Users.Add(_user);
        _db.SaveChanges();
        _channel = new EmailNotificationChannel(_db, _email.Object, NullLogger<EmailNotificationChannel>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private UserNotification ExportReady(UserId? user = null, string? downloadUrl = "https://r2.example/x.csv") => new(
        user ?? _user.Id, NotificationTypes.ExportCompleted, NotificationSeverity.Success, "Export ready",
        SourceId: Guid.NewGuid(),
        Data: downloadUrl == null ? null : new Dictionary<string, string> { [NotificationData.DownloadUrl] = downloadUrl });

    [Fact]
    public async Task ExportCompleted_SendsTheExportReadyEmailToTheUser()
    {
        await _channel.SendAsync(ExportReady());

        _email.Verify(e => e.SendExportReadyEmailAsync("user@example.com", "https://r2.example/x.csv"), Times.Once);
    }

    [Fact]
    public async Task ExportCompleted_WithoutDownloadUrl_SendsNothing()
    {
        await _channel.SendAsync(ExportReady(downloadUrl: null));

        _email.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnknownUser_SendsNothing()
    {
        await _channel.SendAsync(ExportReady(UserId.New()));

        _email.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(NotificationTypes.ExportFailed)]
    [InlineData(NotificationTypes.ImportCompleted)]
    [InlineData(NotificationTypes.ImportFailed)]
    public async Task TypesWithoutTemplate_SendNothing(string type)
    {
        await _channel.SendAsync(ExportReady() with { Type = type });

        _email.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenTheEmailProviderFails_DoesNotThrow()
    {
        _email.Setup(e => e.SendExportReadyEmailAsync(It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("resend down"));

        await _channel.SendAsync(ExportReady());
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
