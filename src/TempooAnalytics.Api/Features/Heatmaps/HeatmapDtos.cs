namespace TempooAnalytics.Api.Features.Heatmaps;

public record HeatmapPageDto(string Path, long Clicks, long RageClicks, long DeadClicks);

/// <param name="X">Thousandths of the document width, grouped in cells of 10.</param>
/// <param name="Y">Document pixels from the top, grouped in cells of 20.</param>
public record HeatmapPointDto(int X, int Y, long Count);

public record HeatmapElementDto(string Selector, long Clicks, long RageClicks, long DeadClicks);

/// <param name="Depth">10, 20 … 100.</param>
/// <param name="ReachedPct">Share (0-100) of page views that reached this scroll depth.</param>
public record ScrollDepthDto(int Depth, double ReachedPct);

public record HeatmapDto(
    string Path,
    string Device,
    long TotalClicks,
    IReadOnlyList<HeatmapPointDto> Points,
    IReadOnlyList<HeatmapElementDto> TopElements,
    IReadOnlyList<ScrollDepthDto> Scroll);

public record HeatmapOverlayTokenRequest(string SiteId, string Path);

/// <param name="SiteUrl">Base URL of the customer's site to open with the token in the hash, or null when the
/// site has no allowed origin or domain-like name to derive it from.</param>
public record HeatmapOverlayTokenResponse(string Token, DateTimeOffset ExpiresAt, string? SiteUrl);
