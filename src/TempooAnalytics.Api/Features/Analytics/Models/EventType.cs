using NpgsqlTypes;

namespace TempooAnalytics.Api.Features.Analytics.Models;

public enum EventType
{
    Pageview,
    Custom,
    Identify,
    Payment,
    Leave
}
