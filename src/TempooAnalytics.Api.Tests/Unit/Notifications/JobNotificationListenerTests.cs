using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TempooAnalytics.Api.Features.Notifications;
using TempooAnalytics.Api.Infrastructure.Services;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Notifications;

public class JobNotificationListenerTests
{
    private readonly Mock<IJobNotifier> _jobs = new();
    private readonly JobNotificationListener _listener;
    private readonly Guid _jobId = Guid.NewGuid();

    public JobNotificationListenerTests()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _jobs.Object);
        _listener = new JobNotificationListener(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<JobNotificationListener>.Instance);
    }

    private Task Process(string json) => _listener.ProcessNotificationAsync(json, CancellationToken.None);

    [Fact]
    public async Task ExportCompleted_NotifiesWithTheDownloadUrl()
    {
        await Process($$"""{"type":"EXPORT_COMPLETED","jobId":"{{_jobId}}","siteId":"{{Guid.NewGuid()}}","email":"user@example.com","downloadUrl":"https://r2/x.csv"}""");

        _jobs.Verify(j => j.NotifyExportFinishedAsync(_jobId, true, null, "https://r2/x.csv", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExportFailed_NotifiesWithTheError()
    {
        await Process($$"""{"type":"EXPORT_FAILED","jobId":"{{_jobId}}","siteId":"{{Guid.NewGuid()}}","errorMessage":"upload failed"}""");

        _jobs.Verify(j => j.NotifyExportFinishedAsync(_jobId, false, "upload failed", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportCompleted_NotifiesWithTheRows()
    {
        await Process($$"""{"type":"IMPORT_COMPLETED","jobId":"{{_jobId}}","siteId":"{{Guid.NewGuid()}}","rowsProcessed":"1500"}""");

        _jobs.Verify(j => j.NotifyImportFinishedAsync(_jobId, true, null, 1500, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportFailed_NotifiesWithTheError()
    {
        await Process($$"""{"type":"IMPORT_FAILED","jobId":"{{_jobId}}","siteId":"{{Guid.NewGuid()}}","errorMessage":"bad file"}""");

        _jobs.Verify(j => j.NotifyImportFinishedAsync(_jobId, false, "bad file", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("""{"type":"SOMETHING_NEW","jobId":"9f0c8b0e-6c1f-4a51-9d55-3f4a8e2d1c11"}""")]
    [InlineData("""{"type":"IMPORT_COMPLETED","jobId":"not-a-guid"}""")]
    [InlineData("not json")]
    public async Task UnknownOrInvalidMessages_AreIgnored(string json)
    {
        await Process(json);

        _jobs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OldExportCompletedMessage_WithoutSiteId_StillWorks()
    {
        // Sent by a worker deployed before this change.
        await Process($$"""{"jobId":"{{_jobId}}","email":"user@example.com","downloadUrl":"https://r2/x.csv","type":"EXPORT_COMPLETED"}""");

        _jobs.Verify(j => j.NotifyExportFinishedAsync(_jobId, true, null, "https://r2/x.csv", It.IsAny<CancellationToken>()), Times.Once);
    }
}
