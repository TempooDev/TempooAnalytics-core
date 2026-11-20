using TempooAnalytics.Api.Infrastructure.Common;

namespace TempooAnalytics.Api.Auth;

public class User
{
    public UserId Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsAdmin { get; set; }
    public string? ActivationToken { get; set; }
    public DateTime? ActivationExpires { get; set; }
    public bool IsActive { get; set; }

}
