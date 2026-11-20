using Dapper;

namespace TempooAnalytics.Analytics.Query;

/// <summary>A funnel step: a pageview of a path or a custom event, in order.</summary>
public sealed record FunnelStep(int Order, string? Name, bool IsPageview, string Value);

/// <summary>Sessions that reached a step and the rates the funnel screen shows (percentages, 2 decimals).</summary>
public sealed record FunnelStepResult(int Order, string Name, long Sessions, double DropOffRate, double ConversionRate);

public sealed record FunnelResult(IReadOnlyList<FunnelStepResult> Steps, double OverallConversionRate);

/// <summary>The funnel calculation of the dashboard and the public API: one SQL, so both read the same numbers.</summary>
public interface IFunnelQueries
{
    /// <summary>Sessions of the site in [start, end) that went through the steps in order within 24 hours.</summary>
    Task<FunnelResult> CalculateAsync(Guid siteId, IReadOnlyList<FunnelStep> steps, DateTime start, DateTime end, CancellationToken ct = default);
}

public sealed class FunnelQueries(IClickHouseConnectionFactory connections) : IFunnelQueries
{
    private const int WindowSeconds = 3600 * 24;

    public async Task<FunnelResult> CalculateAsync(Guid siteId, IReadOnlyList<FunnelStep> steps, DateTime start, DateTime end, CancellationToken ct = default)
    {
        if (steps.Count == 0) return new FunnelResult([], 0);
        var ordered = steps.OrderBy(s => s.Order).ToList();

        // windowFunnel(window)(timestamp, cond1, cond2...) is 1 if only step 1 was met, 2 if step 1 and then step 2
        // within the window, and so on. Step values travel as parameters, never inside the SQL text.
        var parameters = new DynamicParameters();
        parameters.Add("siteId", siteId);
        parameters.Add("start", start);
        parameters.Add("end", end);
        var conditions = new List<string>();
        for (var i = 0; i < ordered.Count; i++)
        {
            parameters.Add($"step{i}", ordered[i].Value);
            conditions.Add(ordered[i].IsPageview
                ? $"event_name = 'pageview' AND path = @step{i}"
                : $"event_name = @step{i}");
        }

        var sql = $@"
            SELECT level, count() as count
            FROM (
                SELECT
                    session_id,
                    windowFunnel({WindowSeconds})(timestamp, {string.Join(", ", conditions)}) as level
                FROM tempoo_analytics.events
                WHERE site_id = @siteId AND timestamp >= @start AND timestamp < @end
                GROUP BY session_id
            )
            WHERE level > 0
            GROUP BY level
            ORDER BY level ASC";

        using var conn = connections.Create();
        var levels = (await conn.QueryTracedAsync<LevelRow>(sql, parameters, ct)).ToDictionary(x => (int)x.Level, x => x.Count);

        // Level n means the session met steps 1..n, so a step's sessions are those at that level or deeper.
        var atLeast = new long[ordered.Count + 2];
        for (var i = ordered.Count; i >= 1; i--)
            atLeast[i] = levels.GetValueOrDefault(i) + atLeast[i + 1];
        var first = atLeast[1];

        var results = new List<FunnelStepResult>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var sessions = atLeast[i + 1];
            var previous = i > 0 ? atLeast[i] : 0;
            results.Add(new FunnelStepResult(i + 1, ordered[i].Name ?? $"Step {i + 1}", sessions,
                i > 0 && previous > 0 ? Math.Round(100.0 * (previous - sessions) / previous, 2) : 0,
                first > 0 ? Math.Round(100.0 * sessions / first, 2) : 0));
        }
        return new FunnelResult(results, results.Count > 0 ? results[^1].ConversionRate : 0);
    }

    private sealed class LevelRow
    {
        public long Level { get; set; }
        public long Count { get; set; }
    }
}
