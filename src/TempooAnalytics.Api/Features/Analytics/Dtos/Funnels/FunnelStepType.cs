using System.Text.Json.Serialization;

namespace TempooAnalytics.Api.Features.Analytics.Dtos.Funnels;

[JsonConverter(typeof(JsonStringEnumConverter<FunnelStepType>))]
public enum FunnelStepType
{
    Pageview,
    CustomEvent
}
