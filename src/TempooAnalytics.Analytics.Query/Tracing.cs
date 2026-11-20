using System.Data;
using System.Diagnostics;
using Dapper;

namespace TempooAnalytics.Analytics.Query;

/// <summary>Same spans as the API's ClickHouse calls (source "TempooAnalytics.ClickHouse").</summary>
internal static class Tracing
{
    private static readonly ActivitySource ActivitySource = new("TempooAnalytics.ClickHouse");

    public static async Task<IEnumerable<T>> QueryTracedAsync<T>(this IDbConnection connection, string sql, object? param, CancellationToken ct)
    {
        using var activity = Start("ClickHouse Query", sql);
        return await connection.QueryAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct));
    }

    public static async Task<T> QueryFirstTracedAsync<T>(this IDbConnection connection, string sql, object? param, CancellationToken ct)
    {
        using var activity = Start("ClickHouse QueryFirst", sql);
        return await connection.QueryFirstAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct));
    }

    private static Activity? Start(string name, string sql)
    {
        var activity = ActivitySource.StartActivity(name, ActivityKind.Client);
        activity?.SetTag("db.system", "clickhouse");
        activity?.SetTag("db.statement", sql);
        return activity;
    }
}
