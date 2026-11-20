namespace TempooAnalytics.Api.Features.Analytics.Dtos.Dashboard;

public class BrowserDto
{
    public string Browser { get; set; } = string.Empty;
    public long Value { get; set; }
}

public class OsDto
{
    public string OS { get; set; } = string.Empty;
    public long Value { get; set; }
}

public class ReferrerDto
{
    public string Referrer { get; set; } = string.Empty;
    public long Value { get; set; }
}

public class ReferrerDetailDto
{
    public string Referrer { get; set; } = string.Empty;
    public long Sessions { get; set; }
    public double PercentageSessions { get; set; }
    public long Pageviews { get; set; }
    public double PercentagePageviews { get; set; }
    public double BounceRate { get; set; }
}

public class ChannelDto
{
    public string Channel { get; set; } = string.Empty;
    public long Value { get; set; }
}

public class PageDetailDto
{
    public string Page { get; set; } = string.Empty;
    public long Views { get; set; }
    public double PercentageViews { get; set; }
    public long Visitors { get; set; }
    public double AvgTimeOnPage { get; set; }
    public double BounceRate { get; set; }
}

public class ScreenDto
{
    public string Resolution { get; set; } = string.Empty;
    public long Value { get; set; }
}
