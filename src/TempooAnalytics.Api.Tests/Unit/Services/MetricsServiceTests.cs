using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Infrastructure.Common;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Services;

public class MetricsServiceTests
{
    private readonly Mock<IMetricsRepository> _repoMock = new();
    private readonly MetricsService _service;

    public MetricsServiceTests()
    {
        _service = new MetricsService(_repoMock.Object);
    }

    [Fact]
    public async Task GetSitesSummaryAsync_CallsRepository()
    {
        var userId = UserId.New();
        _repoMock.Setup(r => r.GetSitesSummaryAsync(userId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new List<Features.Analytics.Models.SiteSummary>());
        await _service.GetSitesSummaryAsync(userId, CancellationToken.None);
        _repoMock.Verify(r => r.GetSitesSummaryAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
