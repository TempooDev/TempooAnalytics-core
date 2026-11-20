namespace TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;

public sealed class LocationDto
{
    public string CountryCode { get; set; } = default!;
    public long Value { get; set; }
}
