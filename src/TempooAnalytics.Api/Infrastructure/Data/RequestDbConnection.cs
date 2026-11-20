using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace TempooAnalytics.Api.Infrastructure.Data;

/// <summary>
/// One database connection per scope (request, or one iteration of a worker), shared by <c>AnalyticsDbContext</c>
/// and the cloud edition's module context. Two contexts on the same connection can take part in one transaction: begin it on one
/// and enlist the other with <c>Database.UseTransactionAsync</c> (see <c>UserRepository.BeginTransactionAsync</c>).
/// EF opens the connection for each operation and closes it afterwards (it is not open between them, so the pool
/// still reuses physical connections); during a transaction it stays open.
/// Only one command can run on a connection at a time, so the two contexts of a scope must not be used concurrently.
/// </summary>
public sealed class RequestDbConnection(NpgsqlDataSource dataSource) : IAsyncDisposable, IDisposable
{
    public NpgsqlConnection Connection { get; } = dataSource.CreateConnection();

    public void Dispose() => Connection.Dispose();
    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}

public static class RequestDbConnectionExtensions
{
    // One data source (one connection pool) per connection string for the whole process, like EF's own cache. It is
    // registered as an instance, so the container never disposes it: hosts that come and go (integration tests build
    // one per test) would otherwise leave a pool of idle connections each until PostgreSQL runs out of clients.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, NpgsqlDataSource> DataSources = new();

    /// <summary>Registers the data source (shared per connection string) and the per-scope connection.</summary>
    public static IServiceCollection AddRequestDbConnection(this IServiceCollection services, string connectionString)
    {
        services.RemoveAll<NpgsqlDataSource>();
        services.RemoveAll<RequestDbConnection>();
        services.AddSingleton(DataSources.GetOrAdd(connectionString, cs => NpgsqlDataSource.Create(cs)));
        services.AddScoped<RequestDbConnection>();
        return services;
    }
}
