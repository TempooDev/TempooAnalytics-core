using System.Text.Json.Serialization;

namespace TempooAnalytics.Api.Features.Auth.Dtos;

/// <summary>
/// Profile of the signed-in user. The core only knows identity; modules add their own top-level fields (for example
/// the cloud billing module adds subscription and trial) through <see cref="Extra"/>, which is serialized flat.
/// </summary>
public record UserProfileDto(Guid Id, string Username, string? Email, bool IsAdmin)
{
    [JsonExtensionData]
    public Dictionary<string, object?>? Extra { get; init; }
}
