using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TempooAnalytics.Api.Data;
using TempooAnalytics.Api.Infrastructure.Data;
using TempooAnalytics.Api.Infrastructure.Interceptors;
using Xunit;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System;

[Collection("Integration")]
public abstract class FunctionalTestBase : IAsyncLifetime
{
    protected readonly PostgresFixture Fixture;
    protected readonly HttpClient Client;
    protected readonly WebApplicationFactory<Program> Factory;

    protected FunctionalTestBase(PostgresFixture fixture)
    {
        Fixture = fixture;

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, conf) =>
            {
                var configData = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:analytics-db"] = Fixture.ConnectionString,
                    ["ConnectionStrings:clickhouse"] = "Host=localhost;Port=9000;Database=default",
                    ["Auth:JwtKey"] = "this_key_must_be_at_least_32_characters_long_for_security",
                    ["TEST_EXECUTION"] = "true"
                };
                conf.AddInMemoryCollection(configData);
            });

            builder.ConfigureServices(services =>
            {
                services.AddSingleton<AuditInterceptor>();

                // 1. We find and remove ALL the descriptors related to the API's DbContext
                var dbContextDescriptors = services.Where(d => 
                    d.ServiceType == typeof(DbContextOptions<AnalyticsDbContext>) || 
                    d.ServiceType == typeof(AnalyticsDbContext) ||
                    d.ServiceType.ToString().Contains("PrivateDbContext") ||
                    d.ServiceType.Name.Contains("NpgsqlDataSource")).ToList();

                foreach (var descriptor in dbContextDescriptors)
                {
                    services.Remove(descriptor);
                }

                // 2. We register the DbContext again, forcing the Fixture's connection string.
                // The two contexts share the scope's connection, as in production.
                services.AddRequestDbConnection(Fixture.ConnectionString);
                services.AddDbContext<AnalyticsDbContext>((sp, options) =>
                {
                    options.UseNpgsql(sp.GetRequiredService<RequestDbConnection>().Connection)
                           .UseSnakeCaseNamingConvention()
                           .AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
                });

                // The cloud's private DbContext (module tables) on the same database, when the cloud composition is part of the test host.
                CloudTestHooks.ConfigureServices?.Invoke(services);

                // 3. Remove background workers to avoid interference
                var backgroundTasks = services.Where(d => 
                    d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) ||
                    d.ImplementationType?.Name == "OutboxProcessor" || 
                    d.ImplementationType?.Name == "LeadProcessingWorker" ||
                    d.ImplementationType?.Name == "JobNotificationListener").ToList();
                    
                foreach (var task in backgroundTasks) services.Remove(task);
            });
        });

        Client = Factory.CreateClient();
    }

    public void AuthenticateClient(HttpClient client, Guid userId, bool isAdmin)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes("this_key_must_be_at_least_32_characters_long_for_security");
        var claims = new List<Claim> { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()) };
        if (isAdmin) claims.Add(new Claim("is_admin", "true"));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(7),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenHandler.WriteToken(token));
    }

    public async Task InitializeAsync() => await Fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;
}