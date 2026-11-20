namespace TempooAnalytics.Api.Features.Analytics.Models;

public sealed class DailyMetric
{
    public DateOnly Day { get; set; } // Changed from Date to Day to match the error
    public long Pageviews { get; set; }
    public long Visitors { get; set; }
    public long Events { get; set; } // Changed from CustomEvents to Events
    public double AvgSessionDurationSeconds { get; set; } // Changed to match the error
}
