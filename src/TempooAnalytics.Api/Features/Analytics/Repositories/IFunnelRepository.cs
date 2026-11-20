using TempooAnalytics.Api.Features.Analytics.Dtos.Funnels;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Repositories;

public interface IFunnelRepository
{
    Task<EventMetadataDto> GetEventMetadataAsync(SiteId siteId, DateTime start, DateTime end, CancellationToken ct = default);
    Task<FunnelResultDto> CalculateFunnelAsync(SiteId siteId, List<FunnelStepDto> steps, DateTime start, DateTime end, CancellationToken ct = default);
    Task<FunnelDefinition?> GetFunnelDefinitionAsync(Guid id, SiteId siteId, CancellationToken ct = default);
    Task<Guid> CreateFunnelDefinitionAsync(FunnelDefinition funnel, CancellationToken ct = default);
    Task UpdateFunnelDefinitionAsync(FunnelDefinition funnel, CancellationToken ct = default);
    Task DeleteFunnelDefinitionAsync(FunnelDefinition funnel, CancellationToken ct = default);
    Task<List<FunnelDefinitionDto>> GetFunnelsBySiteAsync(SiteId siteId, CancellationToken ct = default);
}
