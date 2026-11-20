using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Respawn;
using TempooAnalytics.Api.Data;
using Testcontainers.PostgreSql;
using Xunit;

public sealed partial class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:15-alpine")
        .WithDatabase("analytics_test_db")
        // Test-only tuning: a small, throwaway Postgres. Lower memory (the CI runner shares its RAM between
        // jobs) and no fsync (a test database does not need to survive a crash). Production is untouched.
        .WithCommand("-c", "shared_buffers=64MB", "-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off")
        .WithCreateParameterModifier(p => p.HostConfig.Memory = 768L * 1024 * 1024)
        .Build();

    private Respawner? _respawner;
    private DbContextOptions<AnalyticsDbContext> _options = null!;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // 1. Create the Enum type manually before EnsureCreated
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        // This SQL creates the type if it does not exist. 
        // Make sure the values match your EventType enum (they are usually lowercase by default in EF/Postgres)
        var createEnumSql = @"
        DO $$ BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'event_type') THEN
                CREATE TYPE event_type AS ENUM ('pageview', 'custom');
            END IF;
        END $$;";

        await using (var cmd = new NpgsqlCommand(createEnumSql, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Now initialize EF Core
        _options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        using var context = new AnalyticsDbContext(_options);
        await context.Database.MigrateAsync();

        // The cloud's own chain (private tables), when the cloud composition is part of the test host; nothing in the core alone.
        if (CloudTestHooks.MigrateAsync != null) await CloudTestHooks.MigrateAsync(ConnectionString);

        // 3. Configure Respawner (as you already had it)
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = new[] { "public", "billing" }
        });
    }

    public async Task ResetDatabaseAsync()
    {
        if (_respawner != null)
        {
            // Respawn uses a standard ADO.NET connection
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await _respawner.ResetAsync(connection);
        }
    }

    /// <summary>Everything the Postgres server wrote to its log so far (statements too, when <c>log_statement</c> is on).</summary>
    public async Task<string> GetLogsAsync()
    {
        var (stdout, stderr) = await _container.GetLogsAsync();
        return stdout + "\n" + stderr;
    }

    public AnalyticsDbContext CreateContext() => new AnalyticsDbContext(_options);

    public async Task DisposeAsync() => await _container.DisposeAsync();
}