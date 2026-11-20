using System;
using AwesomeAssertions;
using TempooAnalytics.Api.Features.Imports.Models;
using TempooAnalytics.Api.Features.Notifications;
using TempooAnalytics.Api.Infrastructure.Common;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Notifications;

public class NotificationTextsTests
{
    [Theory]
    [InlineData(ImportType.Plausible, "Plausible")]
    [InlineData(ImportType.GoogleAnalytics4, "Google Analytics")]
    public void ImportCompleted_NamesTheSourceTheSiteAndTheRows(ImportType source, string sourceName)
    {
        var text = NotificationTexts.ImportCompleted("example.com", source, 12345);

        text.Title.Should().Be("Import completed");
        text.Body.Should().Be($"Your {sourceName} history for example.com was imported (12,345 rows).");
    }

    [Fact]
    public void ImportFailed_IncludesTheErrorMessage()
    {
        var text = NotificationTexts.ImportFailed("example.com", ImportType.GoogleAnalytics4, "Google denied access.");

        text.Title.Should().Be("Import failed");
        text.Body.Should().Be("The Google Analytics import for example.com failed: Google denied access.");
    }

    [Fact]
    public void ImportFailed_WithoutMessage_StillNamesTheSite()
    {
        NotificationTexts.ImportFailed("example.com", ImportType.Plausible, null).Body
            .Should().Be("The Plausible import for example.com failed.");
    }

    [Fact]
    public void ExportCompleted_PointsToTheEmail()
    {
        var text = NotificationTexts.ExportCompleted("example.com");

        text.Title.Should().Be("Export ready");
        text.Body.Should().Contain("example.com").And.Contain("email");
    }

    [Fact]
    public void ExportFailed_DoesNotExposeTheWorkerError()
    {
        var text = NotificationTexts.ExportFailed("example.com");

        text.Title.Should().Be("Export failed");
        text.Body.Should().Be("The CSV export for example.com failed. Please try again.");
    }

    [Fact]
    public void Links_AreRoutesOfTheApp()
    {
        var siteId = SiteId.New();

        NotificationTexts.ImportsLink(siteId).Should().Be($"/account/sites/{siteId.Value}/imports");
        NotificationTexts.DashboardLink.Should().Be("/");
    }

    [Fact]
    public void ImportRecipient_IsTheUserWhoStartedTheImport()
    {
        var starter = UserId.New();

        JobNotifier.ImportRecipient(starter, UserId.New()).Should().Be(starter);
    }

    [Fact]
    public void ImportRecipient_WithoutUser_IsTheSiteOwner()
    {
        var owner = UserId.New();

        JobNotifier.ImportRecipient(null, owner).Should().Be(owner);
        JobNotifier.ImportRecipient(null, null).Should().BeNull();
    }

    [Fact]
    public void Cursor_RoundTrips()
    {
        var createdAt = new DateTime(2026, 10, 23, 10, 15, 30, 123, DateTimeKind.Utc).AddTicks(4560);
        var id = Guid.NewGuid();

        NotificationEndpoints.TryDecodeCursor(NotificationEndpoints.EncodeCursor(createdAt, id), out var decodedAt, out var decodedId).Should().BeTrue();

        decodedAt.Should().Be(createdAt);
        decodedAt.Kind.Should().Be(DateTimeKind.Utc);
        decodedId.Should().Be(id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-cursor")]
    [InlineData("bm90LWEtZGF0ZXx4eXo")] // "not-a-date|xyz"
    public void Cursor_Invalid_IsRejected(string cursor)
    {
        NotificationEndpoints.TryDecodeCursor(cursor, out _, out _).Should().BeFalse();
    }
}
