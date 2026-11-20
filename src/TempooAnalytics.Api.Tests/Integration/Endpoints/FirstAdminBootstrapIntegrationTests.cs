using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TempooAnalytics.Api.Auth;
using TempooAnalytics.Api.Features.Auth.Dtos;
using TempooAnalytics.Api.Infrastructure.Common;
using TempooAnalytics.Api.Features.Auth.Services;
using TempooAnalytics.Api.Infrastructure.Modules;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace TempooAnalytics.Api.Tests.Integration.Endpoints;

/// <summary>
/// Self-host first run: an empty community instance gets one administrator (from ADMIN_USERNAME/ADMIN_PASSWORD or a generated password
/// logged once); the cloud edition and instances that already have users are left alone.
/// </summary>
public partial class FirstAdminBootstrapIntegrationTests : FunctionalTestBase
{
    private const string EnvPassword = "S3lf-host-env-pass";

    public FirstAdminBootstrapIntegrationTests(PostgresFixture fixture) : base(fixture) { }

    private sealed class CapturingProvider(ConcurrentQueue<string> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(lines);
        public void Dispose() { }
        private sealed class CapturingLogger(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => lines.Enqueue(formatter(state, exception));
        }
    }

    // UseSetting, not ConfigureAppConfiguration: the modules and the first-run step read the settings while the host is being built.
    private HttpClient Start(bool modules, ConcurrentQueue<string> log, params (string Key, string Value)[] settings)
    {
        var factory = Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting(FeatureModuleLoader.EnabledSetting, modules ? "true" : "false");
            foreach (var (k, v) in settings) b.UseSetting(k, v);
            b.ConfigureLogging(l => l.AddProvider(new CapturingProvider(log)));
        });
        return factory.CreateClient();
    }

    private async Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password) =>
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));

    [Fact]
    public async Task Community_WithEnvCredentials_CreatesTheAdminAndNeverLogsThePassword()
    {
        await Fixture.ResetDatabaseAsync();
        var log = new ConcurrentQueue<string>();

        var client = Start(modules: false, log, ("ADMIN_USERNAME", "Owner"), ("ADMIN_PASSWORD", EnvPassword));

        await using var db = Fixture.CreateContext();
        var user = await db.Users.SingleAsync();
        user.Username.Should().Be("owner");
        user.IsAdmin.Should().BeTrue();
        user.IsActive.Should().BeTrue();
        user.Email.Should().BeNull("no welcome email: nothing in the first run depends on the mail provider");
        (await LoginAsync(client, "owner", EnvPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        log.Should().NotContain(l => l.Contains(EnvPassword));
    }

    [Fact]
    public async Task Community_WithoutCredentials_GeneratesAStrongPasswordAndLogsItOnce()
    {
        await Fixture.ResetDatabaseAsync();
        var log = new ConcurrentQueue<string>();

        var client = Start(modules: false, log);

        var line = log.Single(l => l.Contains("FIRST RUN") && l.Contains("Password:"));
        var password = Regex.Match(line, @"Password: (\S+)$").Groups[1].Value;
        password.Length.Should().BeGreaterThanOrEqualTo(24);
        await using var db = Fixture.CreateContext();
        (await db.Users.SingleAsync()).Username.Should().Be("admin");
        (await LoginAsync(client, "admin", password)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(client, "admin", "wrong-password")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Community_WithUsers_DoesNotCreateOrResetAnything()
    {
        await Fixture.ResetDatabaseAsync();
        await using (var db = Fixture.CreateContext())
        {
            db.Users.Add(new User { Id = UserId.New(), Username = "existing", PasswordHash = PasswordHelper.HashPassword("existing-pass"), IsActive = true, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var log = new ConcurrentQueue<string>();

        var client = Start(modules: false, log, ("ADMIN_USERNAME", "existing"), ("ADMIN_PASSWORD", EnvPassword));

        await using var check = Fixture.CreateContext();
        (await check.Users.CountAsync()).Should().Be(1);
        (await LoginAsync(client, "existing", "existing-pass")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(client, "existing", EnvPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "an existing account is never reset from the environment");
        log.Should().NotContain(l => l.Contains("FIRST RUN"));
    }

    [Fact]
    public async Task Community_RestartingIsIdempotent()
    {
        await Fixture.ResetDatabaseAsync();
        var log = new ConcurrentQueue<string>();
        Start(modules: false, log);
        var firstRun = log.Count(l => l.Contains("FIRST RUN"));

        var second = new ConcurrentQueue<string>();
        Start(modules: false, second);

        firstRun.Should().Be(1);
        second.Should().NotContain(l => l.Contains("FIRST RUN"));
        await using var db = Fixture.CreateContext();
        (await db.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ATooShortPasswordCreatesNothing()
    {
        await Fixture.ResetDatabaseAsync();
        var log = new ConcurrentQueue<string>();

        Start(modules: false, log, ("ADMIN_PASSWORD", "short"));

        await using var db = Fixture.CreateContext();
        (await db.Users.CountAsync()).Should().Be(0);
        log.Should().NotContain(l => l.Contains("short") && !l.Contains("at least"));
    }
}
