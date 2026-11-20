using TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;
using TempooAnalytics.Api.Features.Analytics.Models;

namespace TempooAnalytics.Api.Infrastructure.Common;

public static class GapFiller
{
    /// <summary>
    /// Without an explicit breakdown, ranges of up to 24 hours are bucketed by hour, longer ones by day.
    /// ClickHouseRepository.GetDailyMetricsAsync and the gap filler must agree on this, or the filled
    /// series misses every bucket the query returned (days=1 used to come back all zeros).
    /// </summary>
    public static bool IsHourlyByDefault(DateTime start, DateTime end) => (end - start).TotalHours <= 24;

    public static IEnumerable<DailyMetricDto> FillDailyMetricsGaps(IEnumerable<DailyMetricDto> metrics, DateTime start, DateTime end, string? breakdown)
    {
        if (string.IsNullOrEmpty(breakdown) && IsHourlyByDefault(start, end))
        {
            breakdown = "hour";
        }

        var metricsDict = metrics.ToDictionary(m => m.Date, m => m);
        var results = new List<DailyMetricDto>();
        
        DateTime current = AlignStart(start, breakdown);

        while (current < end)
        {
            if (metricsDict.TryGetValue(current, out var m))
            {
                results.Add(m);
            }
            else
            {
                results.Add(new DailyMetricDto { Date = current });
            }
            
            current = AddInterval(current, breakdown);
        }
        return results;
    }

    public static IEnumerable<DailyMetric> FillDailyMetricsGaps(IEnumerable<DailyMetric> metrics, DateTime start, DateTime end)
    {
        var metricsDict = metrics.ToDictionary(m => m.Day, m => m);
        var results = new List<DailyMetric>();
        
        for (var current = start.Date; current < end.Date.AddDays(1); current = current.AddDays(1))
        {
            var day = DateOnly.FromDateTime(current);
            if (metricsDict.TryGetValue(day, out var m))
            {
                results.Add(m);
            }
            else
            {
                results.Add(new DailyMetric { Day = day });
            }
        }
        return results;
    }

    private static DateTime AlignStart(DateTime start, string? breakdown)
    {
        var kind = DateTimeKind.Utc;
        return breakdown?.ToLower() switch
        {
            "hour" => new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0, kind),
            "day" => start.Date,
            "week" => GetStartOfWeek(start.Date),
            "month" => new DateTime(start.Year, start.Month, 1, 0, 0, 0, kind),
            _ => start.Date
        };
    }

    private static DateTime AddInterval(DateTime current, string? breakdown)
    {
        return breakdown?.ToLower() switch
        {
            "hour" => current.AddHours(1),
            "day" => current.AddDays(1),
            "week" => current.AddDays(7),
            "month" => current.AddMonths(1),
            _ => current.AddDays(1)
        };
    }

    private static DateTime GetStartOfWeek(DateTime dt)
    {
        // ClickHouse toStartOfWeek usually uses Monday as start
        int diff = (7 + (dt.DayOfWeek - DayOfWeek.Monday)) % 7;
        return dt.AddDays(-1 * diff).Date;
    }
}
