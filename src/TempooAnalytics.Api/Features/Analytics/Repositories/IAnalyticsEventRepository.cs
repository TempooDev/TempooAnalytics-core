using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Repositories;

public interface IAnalyticsEventRepository
{
    Task<Guid> InsertEventWithOutboxAsync(AnalyticsEventDto dto, CancellationToken ct = default);
    Task<bool> IsIngestAuthorizedAsync(string siteKey, string? writeKey, string? origin, CancellationToken ct = default);
}
