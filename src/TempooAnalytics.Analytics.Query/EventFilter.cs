using System.Text;
using Dapper;

namespace TempooAnalytics.Analytics.Query;

/// <summary>
/// Extra conditions on tempoo_analytics.events (device, country, path...). The dashboard builds them from its
/// filter DTO; the public API has none yet. Imported history can't be filtered by those dimensions, so a
/// non-empty filter leaves it out.
/// </summary>
public sealed class EventFilter(bool hasConditions, Action<StringBuilder, DynamicParameters> appendConditions)
{
    /// <summary>True when at least one condition applies.</summary>
    public bool HasConditions { get; } = hasConditions;

    /// <summary>Appends " AND ..." conditions to a WHERE clause and adds their parameters.</summary>
    public void AppendTo(StringBuilder sql, DynamicParameters parameters) => appendConditions(sql, parameters);
}
