using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TempooAnalytics.Api.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal user)
    {
        var userIdString = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                           ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(userIdString, out var userId) ? userId : null;
    }

    public static Guid GetRequiredUserId(this ClaimsPrincipal user)
    {
        var userId = user.GetUserId();
        if (userId == null)
        {
            throw new UnauthorizedAccessException("User ID not found in claims.");
        }
        return userId.Value;
    }
}
