using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TempooAnalytics.Api.Data;
using Xunit;

namespace TempooAnalytics.Api.Tests.Integration.Schema;

/// <summary>
/// The core chain is one consolidated <c>Initial</c> migration. <see cref="CoreDatabaseMigrator"/> must run it on an empty database and
/// must NOT run it on a database created by the ten-migration chain it replaced (production), where it only records it as applied.
/// The old database is simulated: the tables are the ones <c>Initial</c> creates (the schema is the same, checked against production
/// in <see cref="SchemaVerificationTests"/>) and the history table is rewritten to the ten old ids.
/// </summary>
public class CoreDatabaseMigratorTests : IClassFixture<PostgresFixture>
{
    private static readonly string[] OldCoreIds =
    [
        "20260112194803_InitialIdentityPostgres", "20260207195402_AddPublicShareToken", "20260208022526_AddDurationAndDeviceMetrics",
        "20260210203846_AddFunnelsTable", "20260211184416_AddSiteSoftDelete", "20260218001312_MigratedAnalyticsToClickHouse",
        "20260412234641_AddAuditLogsAndCustomerPortalUrl", "20260519224030_AddFeedbackTable", "20260626094942_AddImportAndExportJobs",
        "20261023234938_AddInAppNotifications"
    ];

    private readonly PostgresFixture _fixture;

    public CoreDatabaseMigratorTests(PostgresFixture fixture) => _fixture = fixture;

    private async Task<string> NewDatabaseAsync()
    {
        var name = $"coremig_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(_fixture.ConnectionString);
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
        await cmd.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = name, Pooling = false }.ConnectionString;
    }

    private static AnalyticsDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<AnalyticsDbContext>().UseNpgsql(connectionString).UseSnakeCaseNamingConvention().Options);

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> HistoryAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT migration_id FROM public.\"__EFMigrationsHistory\" ORDER BY migration_id", connection);
        var ids = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) ids.Add(reader.GetString(0));
        return ids;
    }

    private static async Task MigrateAsync(string connectionString)
    {
        await using var context = Context(connectionString);
        await CoreDatabaseMigrator.MigrateAsync(context, NullLogger.Instance);
    }

    /// <summary>Builds the core tables with the plain chain, then makes the history look like the old chain's: ten old ids, no Initial.</summary>
    private async Task<string> OldChainDatabaseAsync()
    {
        var connectionString = await NewDatabaseAsync();
        await using (var context = Context(connectionString)) await context.Database.MigrateAsync();
        await ExecuteAsync(connectionString, "DELETE FROM public.\"__EFMigrationsHistory\"");
        foreach (var id in OldCoreIds)
            await ExecuteAsync(connectionString, $"INSERT INTO public.\"__EFMigrationsHistory\" (migration_id, product_version) VALUES ('{id}', '10.0.1')");
        return connectionString;
    }

    [Fact]
    public async Task EmptyDatabase_RunsInitial()
    {
        var connectionString = await NewDatabaseAsync();

        await MigrateAsync(connectionString);

        var history = await HistoryAsync(connectionString);
        history.Should().ContainSingle().Which.Should().EndWith("_Initial");
        (await SchemaSnapshot.TakeAsync(connectionString)).Should().Contain("table public.analytics_users");
    }

    [Fact]
    public async Task OldChainDatabase_RecordsInitialWithoutRunningItAndKeepsTheOldRows()
    {
        var connectionString = await OldChainDatabaseAsync();
        var schemaBefore = await SchemaSnapshot.TakeAsync(connectionString);
        // A row that Initial would have to recreate (its table is there): if the migration ran, this would fail or be lost.
        await ExecuteAsync(connectionString, "INSERT INTO public.analytics_sites (id, site_key, name, created_at, is_deleted) VALUES (gen_random_uuid(), 'old-site', 'Old site', now(), false)");

        await MigrateAsync(connectionString);

        var history = await HistoryAsync(connectionString);
        history.Should().HaveCount(OldCoreIds.Length + 1);
        history.Where(id => !id.EndsWith("_Initial", StringComparison.Ordinal)).Should().BeEquivalentTo(OldCoreIds);
        history.Should().ContainSingle(id => id.EndsWith("_Initial", StringComparison.Ordinal));
        (await SchemaSnapshot.TakeAsync(connectionString)).Should().Be(schemaBefore);
        await using var context = Context(connectionString);
        (await context.AnalyticsSites.CountAsync(s => s.SiteKey == "old-site")).Should().Be(1);
    }

    [Fact]
    public async Task OldChainDatabase_SecondRunChangesNothing()
    {
        var connectionString = await OldChainDatabaseAsync();
        await MigrateAsync(connectionString);
        var history = await HistoryAsync(connectionString);
        var schema = await SchemaSnapshot.TakeAsync(connectionString);

        await MigrateAsync(connectionString);

        (await HistoryAsync(connectionString)).Should().Equal(history);
        (await SchemaSnapshot.TakeAsync(connectionString)).Should().Be(schema);
    }

    [Fact]
    public async Task CoreTablesWithoutTheOldLastMigration_Throws()
    {
        var connectionString = await OldChainDatabaseAsync();
        await ExecuteAsync(connectionString, $"DELETE FROM public.\"__EFMigrationsHistory\" WHERE migration_id = '{CoreDatabaseMigrator.LegacyLastMigrationId}'");

        var act = () => MigrateAsync(connectionString);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{CoreDatabaseMigrator.LegacyLastMigrationId}*");
        (await HistoryAsync(connectionString)).Should().HaveCount(OldCoreIds.Length - 1);
    }

    [Fact]
    public async Task CoreTablesWithAnEmptyHistory_Throws()
    {
        var connectionString = await OldChainDatabaseAsync();
        await ExecuteAsync(connectionString, "DELETE FROM public.\"__EFMigrationsHistory\"");

        var act = () => MigrateAsync(connectionString);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await HistoryAsync(connectionString)).Should().BeEmpty();
    }

    [Fact]
    public async Task OldHistoryWithoutCoreTables_Throws()
    {
        var connectionString = await NewDatabaseAsync();
        await ExecuteAsync(connectionString, "CREATE TABLE IF NOT EXISTS public.\"__EFMigrationsHistory\" (migration_id varchar(150) PRIMARY KEY, product_version varchar(32) NOT NULL)");
        await ExecuteAsync(connectionString, $"INSERT INTO public.\"__EFMigrationsHistory\" VALUES ('{CoreDatabaseMigrator.LegacyLastMigrationId}', '10.0.1')");

        var act = () => MigrateAsync(connectionString);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*refusing to guess*");
    }
}
