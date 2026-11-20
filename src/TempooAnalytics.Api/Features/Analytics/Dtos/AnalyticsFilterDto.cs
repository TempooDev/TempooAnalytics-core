namespace TempooAnalytics.Api.Features.Analytics.Dtos;

public class AnalyticsFilterDto
{
    public string? DeviceType { get; set; }
    public string? OS { get; set; }
    public string? Browser { get; set; }
    public string? CountryCode { get; set; }
    public string? Path { get; set; }
    public string? ReferrerDomain { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }

    public List<AdvancedFilterDto> AdvancedFilters { get; set; } = new();
}

public class AdvancedFilterDto
{
    public string Property { get; set; } = string.Empty;
    public string Operator { get; set; } = "is"; // is, is_not, contains, not_contains, matches, not_matches
    public List<string> Values { get; set; } = new();
}
