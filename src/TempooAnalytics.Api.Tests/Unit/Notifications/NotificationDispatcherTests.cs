using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TempooAnalytics.Api.Features.Notifications;
using TempooAnalytics.Api.Features.Notifications.Models;
using TempooAnalytics.Api.Infrastructure.Common;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Notifications;

public class NotificationDispatcherTests
{
    private readonly Mock<INotificationChannel> _inApp = Channel(NotificationChannels.InApp);
    private readonly Mock<INotificationChannel> _email = Channel(NotificationChannels.Email);
    private readonly NotificationDispatcher _dispatcher;
    private readonly UserNotification _notification = new(UserId.New(), NotificationTypes.ExportCompleted, NotificationSeverity.Success, "Export ready");

    public NotificationDispatcherTests()
    {
        _dispatcher = new NotificationDispatcher([_inApp.Object, _email.Object], NullLogger<NotificationDispatcher>.Instance);
    }

    private static Mock<INotificationChannel> Channel(NotificationChannels channel)
    {
        var mock = new Mock<INotificationChannel>();
        mock.Setup(c => c.Channel).Returns(channel);
        return mock;
    }

    [Fact]
    public async Task Dispatch_ByDefault_OnlyUsesThePanel()
    {
        await _dispatcher.DispatchAsync(_notification);

        _inApp.Verify(c => c.SendAsync(_notification, It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(c => c.SendAsync(It.IsAny<UserNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Dispatch_ToSeveralChannels_UsesEachOnce()
    {
        await _dispatcher.DispatchAsync(_notification, NotificationChannels.InApp | NotificationChannels.Email);

        _inApp.Verify(c => c.SendAsync(_notification, It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(c => c.SendAsync(_notification, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dispatch_WhenAChannelThrows_StillUsesTheOthers_AndDoesNotThrow()
    {
        _inApp.Setup(c => c.SendAsync(It.IsAny<UserNotification>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        var act = () => _dispatcher.DispatchAsync(_notification, NotificationChannels.InApp | NotificationChannels.Email);

        await act.Should().NotThrowAsync();
        _email.Verify(c => c.SendAsync(_notification, It.IsAny<CancellationToken>()), Times.Once);
    }
}
