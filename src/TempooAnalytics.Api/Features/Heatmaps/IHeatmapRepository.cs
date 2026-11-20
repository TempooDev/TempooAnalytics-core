using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Features.Heatmaps;

public interface IHeatmapRepository
{
    Task<IReadOnlyList<HeatmapPageDto>> GetPagesAsync(SiteId siteId, DateTime start, DateTime end, string device, CancellationToken ct = default);
    Task<IReadOnlyList<HeatmapPointDto>> GetPointsAsync(SiteId siteId, string path, DateTime start, DateTime end, string device, CancellationToken ct = default);
    Task<IReadOnlyList<HeatmapElementDto>> GetTopElementsAsync(SiteId siteId, string path, DateTime start, DateTime end, string device, CancellationToken ct = default);
    Task<long> GetTotalClicksAsync(SiteId siteId, string path, DateTime start, DateTime end, string device, CancellationToken ct = default);
    /// <summary>Leave events of the page by scroll_depth (0-100).</summary>
    Task<IReadOnlyList<(int Depth, long Views)>> GetScrollHistogramAsync(SiteId siteId, string path, DateTime start, DateTime end, string device, CancellationToken ct = default);
}
