using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Imports.Models;
using TempooAnalytics.Api.Features.Imports.Services;
using TempooAnalytics.Api.Features.Notifications;
using TempooAnalytics.Api.Infrastructure.Common;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Imports;

public class ImportServiceEnqueueTests
{
    [Fact]
    public async Task EnqueueCsvImport_WhenTheQueueAndMarkingFailedBothFail_RethrowsTheQueueError()
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase($"imports-{Guid.NewGuid()}")
            .Options;
        // EF InMemory can't run ExecuteUpdate, so MarkFailed throws too.
        using var db = new InMemoryAnalyticsDbContext(options);
        var queue = new Mock<IImportQueue>();
        queue.Setup(q => q.EnqueueAsync(It.IsAny<ImportTaskPayload>())).ThrowsAsync(new TimeoutException("redis down"));
        var service = new ImportService(db, queue.Object, TimeProvider.System, Mock.Of<IJobNotifier>(), NullLogger<ImportService>.Instance);

        var act = () => service.EnqueueCsvImport(SiteId.New(), "imports/s/a.zip", ImportType.Plausible, UserId.New());

        await act.Should().ThrowAsync<TimeoutException>().WithMessage("redis down");
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
