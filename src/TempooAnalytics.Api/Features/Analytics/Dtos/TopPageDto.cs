namespace TempooAnalytics.Api.Features.Analytics.Dtos;

public sealed class TopPageDto
{
    public string Url { get; set; } = string.Empty;
    public long Views { get; set; }
    public double AvgTimeOnPage { get; set; }
}
