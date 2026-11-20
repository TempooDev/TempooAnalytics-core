using System.Data;
using System.Diagnostics;
using Dapper;

namespace TempooAnalytics.Api.Infrastructure.Tracing;

public static class ClickHouseTracingExtensions
{
    private static readonly ActivitySource ActivitySource = new("TempooAnalytics.ClickHouse");

    public static async Task<IEnumerable<T>> QueryWithTracingAsync<T>(this IDbConnection connection, string sql, object? param = null)
    {
        using var activity = ActivitySource.StartActivity("ClickHouse Query", ActivityKind.Client);
        activity?.SetTag("db.system", "clickhouse");
        activity?.SetTag("db.statement", sql);
        
        return await connection.QueryAsync<T>(sql, param);
    }

    public static async Task<T> QueryFirstWithTracingAsync<T>(this IDbConnection connection, string sql, object? param = null)
    {
        using var activity = ActivitySource.StartActivity("ClickHouse QueryFirst", ActivityKind.Client);
        activity?.SetTag("db.system", "clickhouse");
        activity?.SetTag("db.statement", sql);
        
        return await connection.QueryFirstAsync<T>(sql, param);
    }

    public static async Task<T?> ExecuteScalarWithTracingAsync<T>(this IDbConnection connection, string sql, object? param = null)
    {
        using var activity = ActivitySource.StartActivity("ClickHouse ExecuteScalar", ActivityKind.Client);
        activity?.SetTag("db.system", "clickhouse");
        activity?.SetTag("db.statement", sql);
        
        return await connection.ExecuteScalarAsync<T>(sql, param);
    }

    public static async Task<int> ExecuteWithTracingAsync(this IDbConnection connection, string sql, object? param = null)
    {
        using var activity = ActivitySource.StartActivity("ClickHouse Execute", ActivityKind.Client);
        activity?.SetTag("db.system", "clickhouse");
        activity?.SetTag("db.statement", sql);
        
        return await connection.ExecuteAsync(sql, param);
    }

    public static async Task<T?> ExecuteScalarWithTracingAsync<T>(this IDbConnection connection, CommandDefinition command)
    {
        using var activity = ActivitySource.StartActivity("ClickHouse ExecuteScalar", ActivityKind.Client);
        activity?.SetTag("db.system", "clickhouse");
        activity?.SetTag("db.statement", command.CommandText);
        
        return await connection.ExecuteScalarAsync<T>(command);
    }

    public static async Task<int> ExecuteWithTracingAsync(this IDbConnection connection, CommandDefinition command)
    {
        using var activity = ActivitySource.StartActivity("ClickHouse Execute", ActivityKind.Client);
        activity?.SetTag("db.system", "clickhouse");
        activity?.SetTag("db.statement", command.CommandText);
        
        return await connection.ExecuteAsync(command);
    }
}
