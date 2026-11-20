using ClickHouse.Client.ADO;
using Dapper;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Shared.Telemetry;

namespace TempooAnalytics.Api.Features.Heatmaps;

public class HeatmapRepository(IConfiguration configuration) : IHeatmapRepository
{
    private readonly string _clickHouseConnString = configuration.GetConnectionString("clickhouse")
        ?? throw new InvalidOperationException("ClickHouse connection string is missing");

    private ClickHouseConnection CreateConnection() => new(_clickHouseConnString);

    private const string ClickFilter = @"
        site_id = @siteId AND date >= toDate(@start) AND date < toDate(@end) AND device_type = @device";

    public async Task<IReadOnlyList<HeatmapPageDto>> GetPagesAsync(SiteId siteId, DateTime start, DateTime end, string device, CancellationToken ct = default)
    {
        using var act = TelemetryExtensions.StartActivity("repo.heatmaps.pages");
        var sql = $@"
            SELECT path AS Path, toInt64(count()) AS Clicks,
                   toInt64(countIf(kind = 'rage')) AS RageClicks, toInt64(countIf(kind = 'dead')) AS DeadClicks
            FROM tempoo_analytics.heatmap_clicks
            WHERE {ClickFilter}
            GROUP BY path
            ORDER BY Clicks DESC
            LIMIT {HeatmapGrid.MaxPages}";

        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync<(string Path, long Clicks, long RageClicks, long DeadClicks)>(sql, new { siteId = siteId.Value, start, end, device });
        return rows.Select(r => new HeatmapPageDto(r.Path, r.Clicks, r.RageClicks, r.DeadClicks)).ToList();
    }

    public async Task<IReadOnlyList<HeatmapPointDto>> GetPointsAsync(SiteId siteId, string path, DateTime start, DateTime end, string device, CancellationToken ct = default)
    {
        using var act = TelemetryExtensions.StartActivity("repo.heatmaps.points");
        var sql = $@"
            SELECT toInt32(intDiv(x, {HeatmapGrid.CellX}) * {HeatmapGrid.CellX}) AS X,
                   toInt32(intDiv(y, {HeatmapGrid.CellY}) * {HeatmapGrid.CellY}) AS Y,
                   toInt64(count()) AS Count
            FROM tempoo_analytics.heatmap_clicks
            WHERE {ClickFilter} AND path = @path
            GROUP BY X, Y
            ORDER BY Count DESC
            LIMIT {HeatmapGrid.MaxCells}";

        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync<(int X, int Y, long Count)>(sql, new { siteId = siteId.Value, path, start, end, device });
        return rows.Select(r => new HeatmapPointDto(r.X, r.Y, r.Count)).ToList();
    }

    public async Task<IReadOnlyList<HeatmapElementDto>> GetTopElementsAsync(SiteId siteId, string path, DateTime start, DateTime end, string device, CancellationToken ct = default)
    {
        using var act = TelemetryExtensions.StartActivity("repo.heatmaps.elements");
        var sql = $@"
            SELECT selector AS Selector, toInt64(count()) AS Clicks,
                   toInt64(countIf(kind = 'rage')) AS RageClicks, toInt64(countIf(kind = 'dead')) AS DeadClicks
            FROM tempoo_analytics.heatmap_clicks
            WHERE {ClickFilter} AND path = @path AND selector != ''
            GROUP BY selector
            ORDER BY Clicks DESC
            LIMIT {HeatmapGrid.MaxElements}";

        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync<(string Selector, long Clicks, long RageClicks, long DeadClicks)>(sql, new { siteId = siteId.Value, path, start, end, device });
        return rows.Select(r => new HeatmapElementDto(r.Selector, r.Clicks, r.RageClicks, r.DeadClicks)).ToList();
    }

    public async Task<long> GetTotalClicksAsync(SiteId siteId, string path, DateTime start, DateTime end, string device, CancellationToken ct = default)
    {
        var sql = $@"SELECT toInt64(count()) FROM tempoo_analytics.heatmap_clicks WHERE {ClickFilter} AND path = @path";
        await using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<long>(sql, new { siteId = siteId.Value, path, start, end, device });
    }

    public async Task<IReadOnlyList<(int Depth, long Views)>> GetScrollHistogramAsync(SiteId siteId, string path, DateTime start, DateTime end, string device, CancellationToken ct = default)
    {
        using var act = TelemetryExtensions.StartActivity("repo.heatmaps.scroll");
        var (minWidth, maxWidth) = HeatmapDevices.WidthRange(device);
        // The tracker adds scroll_depth to the first leave event of each page view; the device comes from the viewport width,
        // like heatmap_clicks.device_type.
        const string sql = @"
            SELECT toInt32(least(JSONExtractUInt(data, 'scroll_depth'), 100)) AS Depth, toInt64(count()) AS Views
            FROM tempoo_analytics.events
            WHERE site_id = @siteId AND event_name = 'leave' AND path = @path
              AND timestamp >= @start AND timestamp < @end
              AND traffic_type = 1
              AND screen_width BETWEEN @minWidth AND @maxWidth
              AND JSONHas(data, 'scroll_depth')
            GROUP BY Depth";

        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync<(int Depth, long Views)>(sql, new { siteId = siteId.Value, path, start, end, minWidth, maxWidth });
        return rows.ToList();
    }
}
