using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Auth;

namespace TempooAnalytics.Api.Features.Feedback.Models;

public class Feedback
{
    public Guid Id { get; set; }
    public UserId UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Navigation property
    public User? User { get; set; }
}
