using System.Data;
using ClickHouse.Client.ADO;

namespace TempooAnalytics.Analytics.Query;

/// <summary>Opens ClickHouse connections with the service's own connection string (and so its own user).</summary>
public interface IClickHouseConnectionFactory
{
    IDbConnection Create();
}

public sealed class ClickHouseConnectionFactory(string connectionString) : IClickHouseConnectionFactory
{
    public IDbConnection Create() => new ClickHouseConnection(connectionString);
}
