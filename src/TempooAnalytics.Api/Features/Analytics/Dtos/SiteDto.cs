namespace TempooAnalytics.Api.Features.Analytics.Dtos;

public class SiteDto
{
    public Guid Id { get; set; }
    public string SiteKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string? WriteKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? OwnerId { get; set; }
    public string[]? AllowedOrigins { get; set; }
}
