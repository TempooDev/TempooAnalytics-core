using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Services;

public interface IIngestionService : IService
{
    Task<Result> ProcessEventAsync(AnalyticsEventDto dto, HttpRequest request, CancellationToken ct);
}
