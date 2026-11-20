using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Npgsql;

namespace TempooAnalytics.Api.Tests.Integration.Schema;

/// <summary>
/// A normalized text dump of the schema of a PostgreSQL database: schemas, tables and views, columns (type, nullability,
/// default), constraints, indexes, view definitions, functions and triggers. Two databases built by different routes
/// (different migration chains, or a production copy) have the same schema when their snapshots are equal.
/// Column order, row data and the migrations history are not part of it.
/// </summary>
public static class SchemaSnapshot
{
    private const string UserSchemas = "n.nspname NOT LIKE 'pg\\_%' AND n.nspname <> 'information_schema'";

    public static async Task<string> TakeAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var lines = new List<string>();

        lines.AddRange(await QueryAsync(connection, $"SELECT 'schema ' || n.nspname FROM pg_namespace n WHERE {UserSchemas}"));

        // Tables, views and materialized views.
        lines.AddRange(await QueryAsync(connection, $"""
            SELECT CASE c.relkind WHEN 'r' THEN 'table ' WHEN 'p' THEN 'table ' WHEN 'v' THEN 'view ' ELSE 'matview ' END || n.nspname || '.' || c.relname
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p', 'v', 'm') AND {UserSchemas}
            """));

        lines.AddRange(await QueryAsync(connection, $"""
            SELECT 'column ' || n.nspname || '.' || c.relname || '.' || a.attname || ' ' || format_type(a.atttypid, a.atttypmod)
                   || CASE WHEN a.attnotnull THEN ' NOT NULL' ELSE '' END
                   || COALESCE(' DEFAULT ' || pg_get_expr(d.adbin, d.adrelid), '')
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
            WHERE a.attnum > 0 AND NOT a.attisdropped AND c.relkind IN ('r', 'p', 'v', 'm') AND {UserSchemas}
            """));

        lines.AddRange(await QueryAsync(connection, $"""
            SELECT 'constraint ' || n.nspname || '.' || c.relname || ' ' || k.conname || ' ' || pg_get_constraintdef(k.oid)
            FROM pg_constraint k
            JOIN pg_class c ON c.oid = k.conrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE k.contype <> 'n' AND {UserSchemas}
            """));

        lines.AddRange(await QueryAsync(connection, $"""
            SELECT 'index ' || pg_get_indexdef(i.indexrelid)
            FROM pg_index i
            JOIN pg_class c ON c.oid = i.indrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE {UserSchemas}
            """));

        lines.AddRange(await QueryAsync(connection, $"""
            SELECT 'viewdef ' || n.nspname || '.' || c.relname || ' ' || regexp_replace(pg_get_viewdef(c.oid), '\s+', ' ', 'g')
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('v', 'm') AND {UserSchemas}
            """));

        lines.AddRange(await QueryAsync(connection, $"""
            SELECT 'trigger ' || regexp_replace(pg_get_triggerdef(t.oid), '\s+', ' ', 'g')
            FROM pg_trigger t
            JOIN pg_class c ON c.oid = t.tgrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE NOT t.tgisinternal AND {UserSchemas}
            """));

        lines.AddRange(await QueryAsync(connection, $"""
            SELECT 'function ' || n.nspname || '.' || p.proname || '(' || pg_get_function_identity_arguments(p.oid) || ') '
                   || md5(pg_get_functiondef(p.oid))
            FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE {UserSchemas}
            """));

        var text = new StringBuilder();
        foreach (var line in lines.Where(l => !l.Contains("__EFMigrationsHistory") && !IsNotEfs(l)).OrderBy(l => l, StringComparer.Ordinal))
            text.Append(line).Append('\n');
        return text.ToString();
    }

    // Not part of what the migrations manage: the hangfire schema (created by Hangfire itself in production), the pg_stat_statements
    // extension, and the schema that only holds the private migrations history.
    private static readonly Regex NotEfs = new(@"\bhangfire\b|pg_stat_statements|^schema private$", RegexOptions.Compiled);
    private static bool IsNotEfs(string line) => NotEfs.IsMatch(line);

    // Objects of the feature modules (billing, AI, public API, marketing): the cloud's private chain creates them.
    private static readonly Regex Module = new(@"\b(ai|billing|public_api|marketing)\.|^schema (ai|billing|public_api|marketing)$", RegexOptions.Compiled);

    /// <summary>The lines of a snapshot that belong to the feature modules (none in a database built by the core chain alone).</summary>
    public static string ModuleObjects(string snapshot) => string.Join("\n", snapshot.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => Module.IsMatch(l)));

    /// <summary>The snapshot without the lines of the feature modules: what a core-only database is compared with.</summary>
    public static string WithoutModuleObjects(string snapshot) => string.Join("\n", snapshot.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !Module.IsMatch(l))) + "\n";

    /// <summary>The lines only one of the two snapshots has: «-» only in <paramref name="expected"/>, «+» only in <paramref name="actual"/>.</summary>
    public static string Diff(string expected, string actual)
    {
        var e = expected.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var a = actual.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var diff = new StringBuilder();
        foreach (var line in e.Except(a).OrderBy(l => l, StringComparer.Ordinal)) diff.Append("- ").Append(line).Append('\n');
        foreach (var line in a.Except(e).OrderBy(l => l, StringComparer.Ordinal)) diff.Append("+ ").Append(line).Append('\n');
        return diff.ToString();
    }

    private static async Task<List<string>> QueryAsync(NpgsqlConnection connection, string sql)
    {
        var rows = new List<string>();
        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return rows;
    }
}
