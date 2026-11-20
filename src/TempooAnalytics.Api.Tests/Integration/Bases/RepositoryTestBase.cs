using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using TempooAnalytics.Api.Data;
using Xunit;

[Collection("Integration")]
public abstract class RepositoryTestBase : IAsyncLifetime
{
    protected readonly PostgresFixture Fixture;
    protected AnalyticsDbContext Context { get; private set; }
    protected IConfiguration Configuration { get; private set; }
    protected RepositoryTestBase(PostgresFixture fixture)
    {
        Fixture = fixture;
        Context = Fixture.CreateContext();
        Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:analytics-db"] = Fixture.ConnectionString,
                ["Auth:JwtKey"] = "this_key_must_be_at_least_32_characters_long_for_security"
            })
            .Build();
    }

    public async Task InitializeAsync()
    {
        // Reset the data before each test (fast with Respawner)
        await Fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Context.DisposeAsync().AsTask();
}