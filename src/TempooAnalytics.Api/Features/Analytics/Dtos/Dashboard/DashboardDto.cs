namespace TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;

using TempooAnalytics.Api.Features.Analytics.Dtos;
using TempooAnalytics.Api.Features.Analytics.Dtos.Funnels;

public sealed class DashboardDto
{
    public IEnumerable<DailyMetricDto> DailyMetrics { get; set; } = new DailyMetricDto[0];
    public IEnumerable<TopSiteDto> TopSites { get; set; } = new TopSiteDto[0];
    public IEnumerable<TopPageDto> TopPages { get; set; } = new TopPageDto[0];
    public IEnumerable<TopPageDto> PageTitles { get; set; } = new TopPageDto[0];
    public IEnumerable<TopPageDto> EntryPages { get; set; } = new TopPageDto[0];
    public IEnumerable<TopPageDto> ExitPages { get; set; } = new TopPageDto[0];
    public IEnumerable<LocationDto> Locations { get; set; } = new LocationDto[0];
    public IEnumerable<DeviceDto> Devices { get; set; } = new DeviceDto[0];
    public IEnumerable<BrowserDto> Browsers { get; set; } = new BrowserDto[0];
    public IEnumerable<OsDto> OS { get; set; } = new OsDto[0];
    public IEnumerable<ReferrerDto> Referrers { get; set; } = new ReferrerDto[0];
    public IEnumerable<ChannelDto> Channels { get; set; } = new ChannelDto[0];
    public IEnumerable<ScreenDto> Screens { get; set; } = new ScreenDto[0];
    public IEnumerable<EventSummaryDto> CustomEvents { get; set; } = new EventSummaryDto[0];
    public IEnumerable<FunnelDefinitionDto> Funnels { get; set; } = new FunnelDefinitionDto[0];
    
    public IEnumerable<DimensionValueDto> UtmSources { get; set; } = new DimensionValueDto[0];
    public IEnumerable<DimensionValueDto> UtmMediums { get; set; } = new DimensionValueDto[0];
    public IEnumerable<DimensionValueDto> UtmCampaigns { get; set; } = new DimensionValueDto[0];
    
    public IEnumerable<HourlyActivityDto> WeeklyTrends { get; set; } = new HourlyActivityDto[0];
    public IEnumerable<TechnicalTrafficDto> TechnicalTraffic { get; set; } = new TechnicalTrafficDto[0];
    public IEnumerable<BotTrafficDto> BotTraffic { get; set; } = new BotTrafficDto[0];
    public IEnumerable<CompanyTrafficDto> Companies { get; set; } = new CompanyTrafficDto[0];
    
    public MetricCardDto Metrics30 { get; set; } = new MetricCardDto();
    public MetricCardDto Metrics30Previous { get; set; } = new MetricCardDto();

    /// <summary>The selected range includes imported history (see SiteMetricsSummaryDto).</summary>
    public bool IncludesImportedData { get; set; }

    /// <summary>The selected range has imported history that filters left out.</summary>
    public bool ImportedDataExcludedByFilter { get; set; }
    public int TotalSites { get; set; }
}
