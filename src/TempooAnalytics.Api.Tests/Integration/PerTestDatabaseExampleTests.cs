// using System;
// using System.Threading.Tasks;
// using Microsoft.Extensions.Configuration;
// using Dapper;
// using Npgsql;
// using Xunit;

// namespace TempooAnalytics.Api.Tests.Integration
// {
//     [Collection("Integration")]
//     public class PerTestDatabaseExampleTests
//     {
//         private readonly PostgresFixture _fixture;

//         public PerTestDatabaseExampleTests(PostgresFixture fixture)
//         {
//             _fixture = fixture;
//         }

//         [Fact]
//         public async Task CreatePerTestDatabase_RunsScripts_And_DropWorks()
//         {
//             // Arrange - create an isolated per-test DB
//             var config = await _fixture.CreatePerTestDatabaseAsync("pertest_");
//             var connStr = config.GetConnectionString("analytics-db");
//             Assert.False(string.IsNullOrWhiteSpace(connStr));

//             // Act - open connection and query a seeded table
//             await using var conn = new NpgsqlConnection(connStr);
//             await conn.OpenAsync();

//             // The schema+seed scripts are executed by CreatePerTestDatabaseAsync; verify table exists
//             var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM analytics_events;");

//             // Assert - we got a numeric count (seed may be zero depending on seed script)
//             Assert.True(count >= 0);

//             // Derive DB name from the connection string so we can drop it
//             var builder = new NpgsqlConnectionStringBuilder(connStr);
//             var dbName = builder.Database;
//             Assert.False(string.IsNullOrWhiteSpace(dbName));

//             // Cleanup - drop the per-test database
//             await _fixture.DropPerTestDatabaseAsync(dbName);

//             // Optional: verify the DB was dropped by attempting to connect (should fail)
//             await Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
//             {
//                 await using var shouldFail = new NpgsqlConnection(connStr);
//                 await shouldFail.OpenAsync();
//             });
//         }
//     }
// }
