using System.Data.Common;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace TempooAnalytics.Api.Data;

/// <summary>
/// Applies the core migration chain (<see cref="AnalyticsDbContext"/>, history <c>public.__EFMigrationsHistory</c>) and bridges the
/// databases that were created by the chain this one replaced.
///
/// The core chain is a single consolidated <c>Initial</c> migration. Before it, the core had ten migrations (the last one is
/// <see cref="LegacyLastMigrationId"/>), and a database created by them already has every core table, so running
/// <c>Initial</c> would try to create what exists. For such a database (and only such a database) this class records
/// <c>Initial</c> as applied, without running it; the only thing written is one row in the history table, and the old rows stay
/// as they are (EF ignores applied ids it no longer knows). How it tells:
/// <list type="bullet">
/// <item><c>Initial</c> is not in the history yet, and</item>
/// <item>the core tables exist (<c>public.analytics_users</c>), and</item>
/// <item>the history has <see cref="LegacyLastMigrationId"/>, meaning the old chain ran to its end.</item>
/// </list>
/// Core tables that exist without the old chain's last migration are a database in an unknown state (an old install that
/// stopped half way, or tables created by hand): it throws instead of guessing. A database with no core tables just runs the chain.
/// </summary>
public static class CoreDatabaseMigrator
{
    /// <summary>The last migration of the core chain that <c>Initial</c> replaced.</summary>
    public const string LegacyLastMigrationId = "20261023234938_AddInAppNotifications";

    /// <summary>A table the old core chain and <c>Initial</c> both create; its presence means "the core tables exist".</summary>
    private const string ProbeTable = "public.analytics_users";

    public static async Task MigrateAsync(AnalyticsDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        await BaselineOldDatabaseAsync(db, logger, cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }

    private static async Task BaselineOldDatabaseAsync(AnalyticsDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var initial = db.Database.GetMigrations().First();
        var history = db.GetService<IHistoryRepository>();
        var applied = await history.GetAppliedMigrationsAsync(cancellationToken);
        if (applied.Any(r => r.MigrationId == initial)) return;

        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) await connection.OpenAsync(cancellationToken);
        try
        {
            var coreTablesExist = await ScalarBoolAsync(connection, $"SELECT to_regclass('{ProbeTable}') IS NOT NULL", cancellationToken);
            var oldChainComplete = applied.Any(r => r.MigrationId == LegacyLastMigrationId);

            if (!coreTablesExist)
            {
                if (oldChainComplete)
                    throw new InvalidOperationException(
                        $"The migration history says the old core chain ran (it has {LegacyLastMigrationId}) but the core table {ProbeTable} does not exist; " +
                        "refusing to guess. Restore the database or clear the history table of a database you know is empty.");
                return; // fresh database: Initial creates the tables
            }

            if (!oldChainComplete)
                throw new InvalidOperationException(
                    $"The database has core tables ({ProbeTable}) but its migration history does not contain {LegacyLastMigrationId}, " +
                    "so it is not known which part of the old core chain ran. Bring it to the end of the old chain with the previous release first.");

            var version = typeof(DbContext).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";

            try
            {
                // CREATE TABLE IF NOT EXISTS (a no-op: the history table is there) plus one row, in one transaction.
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), cancellationToken);
                await db.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(initial, version)), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DuplicateTable or PostgresErrorCodes.DuplicateObject)
            {
                // Another instance recorded it first (a rolling deploy): fine as long as the row is there.
                if (!(await history.GetAppliedMigrationsAsync(cancellationToken)).Any(r => r.MigrationId == initial)) throw;
                return;
            }

            logger.LogInformation("Recorded {Migration} as applied in public.__EFMigrationsHistory: the database was created by the old core migration chain and already has the core tables.", initial);
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }

    private static async Task<bool> ScalarBoolAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }
}
