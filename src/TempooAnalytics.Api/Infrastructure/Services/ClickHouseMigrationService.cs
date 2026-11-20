using System.Data;
using ClickHouse.Client.ADO;
using Dapper;

namespace TempooAnalytics.Api.Infrastructure.Services;

public interface IClickHouseMigrationService
{
    Task MigrateAsync(CancellationToken ct = default);
    Task SeedAsync(CancellationToken ct = default);
}

public class ClickHouseMigrationService(
    IConfiguration configuration,
    ILogger<ClickHouseMigrationService> logger,
    IWebHostEnvironment environment) : IClickHouseMigrationService
{
    private readonly string _connectionString = configuration.GetConnectionString("clickhouse") 
                            ?? throw new InvalidOperationException("ClickHouse connection string is missing");

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        var msg = ">>> [CLICKHOUSE MIGRATION] Starting...";
        Console.WriteLine(msg);
        logger.LogInformation(msg);

        try
        {
            using var conn = new ClickHouseConnection(_connectionString);
            await conn.OpenAsync(ct);

            // 0. Ensure Database exists
            await conn.ExecuteAsync("CREATE DATABASE IF NOT EXISTS tempoo_analytics");

            // 1. Ensure migrations table exists
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS tempoo_analytics.schema_migrations (
                    version String,
                    applied_at DateTime DEFAULT now()
                ) ENGINE = MergeTree() ORDER BY version");

            // 2. Get applied migrations
            var applied = (await conn.QueryAsync<string>("SELECT version FROM tempoo_analytics.schema_migrations")).ToHashSet();

            // 3. Find migration scripts
            string? scriptsPath = GetScriptsPath();

            if (scriptsPath == null)
            {
                var error = ">>> [CLICKHOUSE MIGRATION] ERROR: Scripts folder not found in any location!";
                Console.WriteLine(error);
                logger.LogError(error);
                return;
            }

            Console.WriteLine($">>> [CLICKHOUSE MIGRATION] Found scripts at: {scriptsPath}");

            var scripts = Directory.GetFiles(scriptsPath, "*.sql")
                .Where(f => !Path.GetFileName(f).StartsWith("seed_"))
                .OrderBy(f => f)
                .ToList();

            foreach (var scriptPath in scripts)
            {
                var fileName = Path.GetFileName(scriptPath);
                if (applied.Contains(fileName)) continue;

                Console.WriteLine($">>> [CLICKHOUSE MIGRATION] Applying: {fileName}");
                await ExecuteScript(conn, scriptPath, ct);

                await conn.ExecuteAsync("INSERT INTO tempoo_analytics.schema_migrations (version) VALUES (@version)", new { version = fileName });
                Console.WriteLine($">>> [CLICKHOUSE MIGRATION] SUCCESS: {fileName}");
            }

            Console.WriteLine(">>> [CLICKHOUSE MIGRATION] Completed successfully.");
        }
        catch (Exception ex)
        {
            var error = $">>> [CLICKHOUSE MIGRATION] CRITICAL ERROR: {ex.Message}";
            Console.WriteLine(error);
            logger.LogError(ex, error);
            throw;
        }
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        Console.WriteLine(">>> [CLICKHOUSE SEED] Checking if seed is needed...");
        
        try
        {
            using var conn = new ClickHouseConnection(_connectionString);
            await conn.OpenAsync(ct);

            // Check if events table has data
            var hasData = await conn.ExecuteScalarAsync<long>("SELECT count() FROM tempoo_analytics.events");
            if (hasData > 0)
            {
                Console.WriteLine(">>> [CLICKHOUSE SEED] Skip: Data already exists.");
                return;
            }

            string? scriptsPath = GetScriptsPath();
            if (scriptsPath == null) return;

            var seeds = Directory.GetFiles(scriptsPath, "*.sql")
                .Where(f => Path.GetFileName(f).StartsWith("seed_"))
                .OrderBy(f => f)
                .ToList();

            foreach (var seedPath in seeds)
            {
                var fileName = Path.GetFileName(seedPath);
                Console.WriteLine($">>> [CLICKHOUSE SEED] Applying seed: {fileName}");
                await ExecuteScript(conn, seedPath, ct);
                Console.WriteLine($">>> [CLICKHOUSE SEED] SUCCESS: {fileName}");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, ">>> [CLICKHOUSE SEED] Error during seeding: {Message}", ex.Message);
        }
    }

    private string? GetScriptsPath()
    {
        var possiblePaths = new[] {
            Path.Combine(AppContext.BaseDirectory, "Data", "ClickHouseScripts"),
            Path.Combine(environment.ContentRootPath, "Data", "ClickHouseScripts"),
            Path.Combine(AppContext.BaseDirectory, "ClickHouseScripts"), // Standard publish path
            "/app/Data/ClickHouseScripts",
            "/app/ClickHouseScripts"
        };

        foreach (var p in possiblePaths)
        {
            if (Directory.Exists(p)) return p;
        }

        return null;
    }

    private async Task ExecuteScript(ClickHouseConnection conn, string scriptPath, CancellationToken ct)
    {
        var sql = await File.ReadAllTextAsync(scriptPath, ct);
        
        // Split by semicolon followed by newline or end of string
        var statements = System.Text.RegularExpressions.Regex.Split(sql, @";\s*[\r\n]+|;$", System.Text.RegularExpressions.RegexOptions.Multiline);

        foreach (var statement in statements)
        {
            var trimmed = statement.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Simple check to see if the statement is just comments
            // We remove single line comments and check if anything remains
            var withoutComments = System.Text.RegularExpressions.Regex.Replace(trimmed, @"(--.*)|(/\*[\s\S]*?\*/)", "").Trim();
            if (string.IsNullOrWhiteSpace(withoutComments)) continue;

            await conn.ExecuteAsync(trimmed);
        }
    }
}
