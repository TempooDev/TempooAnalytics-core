using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Dtos.Funnels;
using TempooAnalytics.Api.Features.Analytics.Models;
using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Analytics.Services;

public class FunnelService(IFunnelRepository funnelRepository, ISiteRepository siteRepository, ISiteAccessService siteAccess) : IFunnelService
{
    private async Task<Result<SiteId>> ResolveSiteIdAndCheckAccessAsync(UserId userId, string siteIdOrKey, CancellationToken ct)
    {
        SiteId siteId;
        if (Guid.TryParse(siteIdOrKey, out var guid))
        {
            siteId = new SiteId(guid);
        }
        else
        {
            var site = await siteRepository.GetSiteByKeyAsync(siteIdOrKey, ct);
            if (site == null)
                return Result.Failure<SiteId>($"Site not found: {siteIdOrKey}", ResultErrorType.NotFound);
            siteId = site.Id;
        }

        if (!await siteAccess.CanAccessAsync(userId, siteId, ct))
            return Result.Failure<SiteId>("Access denied to this site", ResultErrorType.Forbidden);

        return Result.Success(siteId);
    }

    public async Task<Result<EventMetadataDto>> GetEventMetadataAsync(UserId userId, string siteId, DateTime start, DateTime end, CancellationToken ct)
    {
        var res = await ResolveSiteIdAndCheckAccessAsync(userId, siteId, ct);
        if (!res.IsSuccess) return Result.Failure<EventMetadataDto>(res.ErrorMessage!, res.ErrorType);
        
        var data = await funnelRepository.GetEventMetadataAsync(res.Value, start, end, ct);
        return Result.Success(data);
    }

    public async Task<Result<IEnumerable<FunnelDefinitionDto>>> GetFunnelsBySiteAsync(UserId userId, string siteId, CancellationToken ct)
    {
        var res = await ResolveSiteIdAndCheckAccessAsync(userId, siteId, ct);
        if (!res.IsSuccess) return Result.Failure<IEnumerable<FunnelDefinitionDto>>(res.ErrorMessage!, res.ErrorType);

        var data = await funnelRepository.GetFunnelsBySiteAsync(res.Value, ct);
        return Result.Success<IEnumerable<FunnelDefinitionDto>>(data);
    }

    public async Task<Result<Guid>> CreateFunnelAsync(UserId userId, FunnelDefinitionDto dto, CancellationToken ct)
    {
        if (dto.Steps == null || dto.Steps.Count == 0)
            return Result.Failure<Guid>("Steps required", ResultErrorType.Validation);

        var res = await ResolveSiteIdAndCheckAccessAsync(userId, dto.SiteId, ct);
        if (!res.IsSuccess) return Result.Failure<Guid>(res.ErrorMessage!, res.ErrorType);

        var entity = new FunnelDefinition
        {
            Name = dto.Name,
            SiteId = res.Value,
            Steps = dto.Steps,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var id = await funnelRepository.CreateFunnelDefinitionAsync(entity, ct);
        return Result.Success(id);
    }

    public async Task<Result<bool>> UpdateFunnelAsync(UserId userId, Guid id, FunnelDefinitionDto dto, CancellationToken ct)
    {
        var res = await ResolveSiteIdAndCheckAccessAsync(userId, dto.SiteId, ct);
        if (!res.IsSuccess) return Result.Failure<bool>(res.ErrorMessage!, res.ErrorType);

        var entity = await funnelRepository.GetFunnelDefinitionAsync(id, res.Value, ct);
        if (entity == null)
            return Result.Failure<bool>("Funnel not found", ResultErrorType.NotFound);

        entity.Name = dto.Name;
        entity.Steps = dto.Steps;

        await funnelRepository.UpdateFunnelDefinitionAsync(entity, ct);
        return Result.Success(true);
    }

    public async Task<Result<bool>> DeleteFunnelAsync(UserId userId, Guid id, string siteId, CancellationToken ct)
    {
        var res = await ResolveSiteIdAndCheckAccessAsync(userId, siteId, ct);
        if (!res.IsSuccess) return Result.Failure<bool>(res.ErrorMessage!, res.ErrorType);

        var entity = await funnelRepository.GetFunnelDefinitionAsync(id, res.Value, ct);
        if (entity == null)
            return Result.Failure<bool>("Funnel not found", ResultErrorType.NotFound);

        await funnelRepository.DeleteFunnelDefinitionAsync(entity, ct);
        return Result.Success(true);
    }

    public async Task<Result<FunnelResultDto>> CalculateFunnelAsync(UserId userId, string siteId, List<FunnelStepDto> steps, DateTime start, DateTime end, CancellationToken ct)
    {
        var res = await ResolveSiteIdAndCheckAccessAsync(userId, siteId, ct);
        if (!res.IsSuccess) return Result.Failure<FunnelResultDto>(res.ErrorMessage!, res.ErrorType);

        var data = await funnelRepository.CalculateFunnelAsync(res.Value, steps, start, end, ct);
        return Result.Success(data);
    }
}
