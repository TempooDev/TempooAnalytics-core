using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Dtos.Funnels;
using TempooAnalytics.Api.Infrastructure;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Services;

public interface IFunnelService : IService
{
    Task<Result<EventMetadataDto>> GetEventMetadataAsync(UserId userId, string siteId, DateTime start, DateTime end, CancellationToken ct);
    Task<Result<IEnumerable<FunnelDefinitionDto>>> GetFunnelsBySiteAsync(UserId userId, string siteId, CancellationToken ct);
    Task<Result<Guid>> CreateFunnelAsync(UserId userId, FunnelDefinitionDto dto, CancellationToken ct);
    Task<Result<bool>> UpdateFunnelAsync(UserId userId, Guid id, FunnelDefinitionDto dto, CancellationToken ct);
    Task<Result<bool>> DeleteFunnelAsync(UserId userId, Guid id, string siteId, CancellationToken ct);
    Task<Result<FunnelResultDto>> CalculateFunnelAsync(UserId userId, string siteId, List<FunnelStepDto> steps, DateTime start, DateTime end, CancellationToken ct);
}
