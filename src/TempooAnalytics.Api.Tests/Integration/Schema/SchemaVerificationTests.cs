using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TempooAnalytics.Api.Data;
using Xunit;
using Xunit.Abstractions;

namespace TempooAnalytics.Api.Tests.Integration.Schema;

/// <summary>
/// Schema verification (3.3 and 3.4 of open-source-core). There are two migration chains: the core's (<c>AnalyticsDbContext</c>,
/// core tables) and the cloud's private one (the module context, module tables); the cloud host runs both, the community host
/// only the core's. The tests here pin what each combination produces:
///
/// 1. <see cref="TheCoreChainAlone_MatchesTheCommittedCoreSchema"/>: a database built by the core chain only has exactly the schema in
///    <c>expected-schema.core.txt</c> (no billing, AI, public API or marketing object). Runs in the public tree too.
/// 2. <see cref="TheCloudChains_MatchTheCommittedSchema"/> (private tree): core chain plus private chain on an empty database give
///    exactly <c>expected-schema.txt</c>, which is the schema production has (verified against a production copy in slice c.6).
/// 3. <see cref="AnotherDatabase_MatchesTheFreshChain"/>: with <c>TEMPOO_COMPARE_CONNECTION</c> set to a connection string (for
///    example a copy of production) it compares that database with the fresh chain(s) and lists the differences; the community
///    variant compares only the core tables. Objects that are not EF's (the hangfire schema, extensions) and the private migrations
///    history are not part of the comparison (see <see cref="SchemaSnapshot"/>).
///
/// To rewrite a committed file after an intended change: run the test with <c>SCHEMA_BASELINE_PATH</c> (full schema) or
/// <c>SCHEMA_BASELINE_CORE_PATH</c> (core schema) set to the file's path.
/// </summary>
public class SchemaVerificationTests : IClassFixture<PostgresFixture>
{
    private const string StartMarker = "<<<SCHEMA-SNAPSHOT";
    private const string EndMarker = "SCHEMA-SNAPSHOT>>>";

    private readonly PostgresFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SchemaVerificationTests(PostgresFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private static bool CloudComposition => CloudTestHooks.MigrateAsync != null;

    /// <summary>A new empty database migrated by the core chain and, when asked and available, by the cloud's.</summary>
    private async Task<string> FreshChainSnapshotAsync(bool withCloud)
    {
        var name = $"schema_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await cmd.ExecuteNonQueryAsync();
        }
        var connectionString = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = name, Pooling = false }.ConnectionString;
        await using (var context = new AnalyticsDbContext(new DbContextOptionsBuilder<AnalyticsDbContext>().UseNpgsql(connectionString).UseSnakeCaseNamingConvention().Options))
            await context.Database.MigrateAsync();
        if (withCloud && CloudTestHooks.MigrateAsync != null) await CloudTestHooks.MigrateAsync(connectionString);
        return await SchemaSnapshot.TakeAsync(connectionString);
    }

    private static string Committed(string suffix)
    {
        var assembly = typeof(SchemaVerificationTests).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    private async Task CheckAgainstCommittedAsync(string actual, string baselineVariable, string resourceSuffix, string what)
    {
        var path = Environment.GetEnvironmentVariable(baselineVariable);
        if (!string.IsNullOrEmpty(path))
        {
            await File.WriteAllTextAsync(path, actual);
            return;
        }

        var diff = SchemaSnapshot.Diff(Committed(resourceSuffix), actual);
        if (diff.Length > 0)
        {
            // The whole snapshot is printed so the committed file can be regenerated from a CI log.
            _output.WriteLine($"{StartMarker}\n{actual}{EndMarker}");
        }
        diff.Should().BeEmpty($"{what} must match {resourceSuffix}; update it on purpose when a migration changes the schema:\n" + diff);
    }

    [Fact]
    public async Task TheCoreChainAlone_MatchesTheCommittedCoreSchema()
    {
        var actual = await FreshChainSnapshotAsync(withCloud: false);

        await CheckAgainstCommittedAsync(actual, "SCHEMA_BASELINE_CORE_PATH", "expected-schema.core.txt", "the schema of a database built by the core chain only");

        SchemaSnapshot.ModuleObjects(actual).Should().BeEmpty("the core chain creates no billing, AI, public API or marketing object");
    }

    [Fact]
    public async Task TheCloudChains_MatchTheCommittedSchema()
    {
        if (!CloudComposition) return; // the cloud's chain only exists in the private tree

        var actual = await FreshChainSnapshotAsync(withCloud: true);

        await CheckAgainstCommittedAsync(actual, "SCHEMA_BASELINE_PATH", "expected-schema.txt", "the schema of a database built by the core and the private chains");
    }

    [Fact]
    public async Task AnotherDatabase_MatchesTheFreshChain()
    {
        var other = Environment.GetEnvironmentVariable("TEMPOO_COMPARE_CONNECTION");
        if (string.IsNullOrEmpty(other)) return; // only when asked: a copy of production, for example

        var diff = SchemaSnapshot.Diff(await FreshChainSnapshotAsync(withCloud: true), await SchemaSnapshot.TakeAsync(other));

        diff.Should().BeEmpty("lines marked «-» are only in a fresh database, «+» only in the compared one:\n" + diff);
    }

    [Fact]
    public async Task AnotherDatabase_MatchesTheCoreChain_ForTheCoreTables()
    {
        var other = Environment.GetEnvironmentVariable("TEMPOO_COMPARE_CONNECTION");
        if (string.IsNullOrEmpty(other)) return;

        // The community host only has the core's tables: compare them, leaving out whatever belongs to the modules.
        var diff = SchemaSnapshot.Diff(await FreshChainSnapshotAsync(withCloud: false), SchemaSnapshot.WithoutModuleObjects(await SchemaSnapshot.TakeAsync(other)));

        diff.Should().BeEmpty("lines marked «-» are only in a database built by the core chain, «+» only in the compared one (module objects left out):\n" + diff);
    }
}
