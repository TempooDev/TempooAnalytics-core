namespace TempooAnalytics.Api.Features.Analytics.Dtos;

public class HourlyActivityDto
{
    public int DayOfWeek { get; set; }
    public int Hour { get; set; }
    public long Count { get; set; }
}
