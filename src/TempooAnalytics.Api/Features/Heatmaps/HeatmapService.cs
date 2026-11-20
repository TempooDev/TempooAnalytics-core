using TempooAnalytics.Api.Features.Analytics.Repositories;
using TempooAnalytics.Api.Features.Analytics.Services;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Plans;
using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Heatmaps;

public interface IHeatmapService
{
    Task<Result<IReadOnlyList<HeatmapPageDto>>> GetPagesAsync(UserId userId, string siteId, DateTime start, DateTime end, string device, CancellationToken ct);
    Task<Result<HeatmapDto>> GetHeatmapAsync(UserId userId, string siteId, string path, DateTime start, DateTime end, string device, CancellationToken ct);
    Task<Result<HeatmapOverlayTokenResponse>> IssueOverlayTokenAsync(UserId userId, HeatmapOverlayTokenRequest request, CancellationToken ct);
    Task<HeatmapDto> GetHeatmapForSiteAsync(SiteId siteId, string path, DateTime start, DateTime end, string device, CancellationToken ct);
}

public class HeatmapService(
    IHeatmapRepository heatmapRepository,
    ISiteRepository siteRepository,
    ISiteAccessService siteAccess,
    IPlanPolicy planPolicy,
    IHeatmapOverlayTokenService overlayTokens) : IHeatmapService
{
    public async Task<Result<IReadOnlyList<HeatmapPageDto>>> GetPagesAsync(UserId userId, string siteId, DateTime start, DateTime end, string device, CancellationToken ct)
    {
        var site = await ResolveAsync(userId, siteId, ct);
        if (!site.IsSuccess) return Result.Failure<IReadOnlyList<HeatmapPageDto>>(site.ErrorMessage!, site.ErrorType);

        return Result.Success(await heatmapRepository.GetPagesAsync(site.Value!.Id, start, end, device, ct));
    }

    public async Task<Result<HeatmapDto>> GetHeatmapAsync(UserId userId, string siteId, string path, DateTime start, DateTime end, string device, CancellationToken ct)
    {
        var site = await ResolveAsync(userId, siteId, ct);
        if (!site.IsSuccess) return Result.Failure<HeatmapDto>(site.ErrorMessage!, site.ErrorType);

        return Result.Success(await GetHeatmapForSiteAsync(site.Value!.Id, path, start, end, device, ct));
    }

    public async Task<Result<HeatmapOverlayTokenResponse>> IssueOverlayTokenAsync(UserId userId, HeatmapOverlayTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Path) || !request.Path.StartsWith('/'))
            return Result.Failure<HeatmapOverlayTokenResponse>("Path must start with /", ResultErrorType.Validation);

        var site = await ResolveAsync(userId, request.SiteId, ct);
        if (!site.IsSuccess) return Result.Failure<HeatmapOverlayTokenResponse>(site.ErrorMessage!, site.ErrorType);

        var (token, expiresAt) = overlayTokens.Issue(site.Value!.Id, request.Path);
        return Result.Success(new HeatmapOverlayTokenResponse(token, expiresAt,
            HeatmapOverlayOrigins.SiteUrl(site.Value.AllowedOrigins, site.Value.Name)));
    }

    public async Task<HeatmapDto> GetHeatmapForSiteAsync(SiteId siteId, string path, DateTime start, DateTime end, string device, CancellationToken ct)
    {
        var total = await heatmapRepository.GetTotalClicksAsync(siteId, path, start, end, device, ct);
        var points = await heatmapRepository.GetPointsAsync(siteId, path, start, end, device, ct);
        var elements = await heatmapRepository.GetTopElementsAsync(siteId, path, start, end, device, ct);
        var scroll = await heatmapRepository.GetScrollHistogramAsync(siteId, path, start, end, device, ct);
        return new HeatmapDto(path, device, total, points, elements, HeatmapGrid.ScrollDistribution(scroll));
    }

    // Site by id or key, then access (owner or member) and the plan of the site's owner (HasAdvancedAnalytics):
    // a member sees the heatmaps of a site whose owner pays for them, whatever their own plan.
    private async Task<Result<Analytics.Models.AnalyticsSite>> ResolveAsync(UserId userId, string siteIdOrKey, CancellationToken ct)
    {
        var site = Guid.TryParse(siteIdOrKey, out var guid)
            ? await siteRepository.GetSiteByIdAsync(new SiteId(guid), ct)
            : await siteRepository.GetSiteByKeyAsync(siteIdOrKey, ct);

        // A site the user cannot see answers like a forbidden one, so site ids cannot be probed.
        if (site is null || !await siteAccess.CanAccessAsync(userId, site.Id, ct))
            return Result.Failure<Analytics.Models.AnalyticsSite>("Access denied to this site", ResultErrorType.Forbidden);

        if (site.OwnerId is not { } ownerId || !await planPolicy.HasAdvancedAnalyticsAsync(ownerId, ct))
            return Result.Failure<Analytics.Models.AnalyticsSite>("Heatmaps need a plan with advanced analytics", ResultErrorType.Forbidden);

        return Result.Success(site);
    }
}
