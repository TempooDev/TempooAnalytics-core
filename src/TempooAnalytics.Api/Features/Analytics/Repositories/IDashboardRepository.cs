using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Repositories;

public interface IDashboardRepository
{
    Task<DashboardDto> GetDashboardAsync(UserId ownerId, DateTime? from = null, DateTime? to = null, int days = 30, int topN = 5, string? siteKey = null, AnalyticsFilterDto? filter = null, string? breakdown = null, CancellationToken ct = default);
    Task<IReadOnlyList<ReferrerDetailDto>> GetReferrersDetailsAsync(UserId ownerId, DateTime? from = null, DateTime? to = null, int days = 30, string? siteKey = null, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<ReferrerDetailDto>> GetChannelsDetailsAsync(UserId ownerId, DateTime? from = null, DateTime? to = null, int days = 30, string? siteKey = null, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<PageDetailDto>> GetPagesDetailsAsync(UserId ownerId, string type, DateTime? from = null, DateTime? to = null, int days = 30, string? siteKey = null, AnalyticsFilterDto? filter = null, CancellationToken ct = default);
    Task<IReadOnlyList<EventDetailDto>> GetEventsDetailsAsync(UserId ownerId, DateTime? from = null, DateTime? to = null, int days = 30, string? siteKey = null, AnalyticsFilterDto? filter = null, int limit = 100, int offset = 0, CancellationToken ct = default);
}
