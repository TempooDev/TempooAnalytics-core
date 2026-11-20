using Microsoft.Extensions.DependencyInjection;

namespace TempooAnalytics.Analytics.Query;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the shared queries over the given ClickHouse connection string.</summary>
    public static IServiceCollection AddAnalyticsQueries(this IServiceCollection services, Func<IServiceProvider, string> connectionString)
    {
        services.AddSingleton<IClickHouseConnectionFactory>(sp => new ClickHouseConnectionFactory(connectionString(sp)));
        services.AddScoped<ISiteMetricsQueries, SiteMetricsQueries>();
        services.AddScoped<IReportQueries, ReportQueries>();
        services.AddScoped<IFunnelQueries, FunnelQueries>();
        return services;
    }
}
