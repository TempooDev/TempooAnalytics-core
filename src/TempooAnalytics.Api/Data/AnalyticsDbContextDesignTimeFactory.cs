using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TempooAnalytics.Api.Data;

public class AnalyticsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AnalyticsDbContext>
{
    public AnalyticsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AnalyticsDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=analytics-db;Username=analytics_user;Password=analytics_password")
                      .UseSnakeCaseNamingConvention();

        return new AnalyticsDbContext(optionsBuilder.Options);
    }
}
