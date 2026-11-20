using System;
using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Moq;
using TempooAnalytics.Api.Features.Auth;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Auth;

public sealed class JwtKeyProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tempoo-jwt-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static IConfiguration Config(params (string Key, string? Value)[] values)
    {
        var data = new Dictionary<string, string?>();
        foreach (var (k, v) in values) data[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    private IHostEnvironment Env(string name)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(name);
        env.SetupGet(e => e.ContentRootPath).Returns(_dir);
        env.SetupGet(e => e.ContentRootFileProvider).Returns(new NullFileProvider());
        return env.Object;
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    public void AConfiguredKeyWinsInEveryEdition(string environment, bool community)
    {
        JwtKeyProvider.Resolve(Config(("Auth:JwtKey", "configured-secret-0123456789-0123456789")), Env(environment), community)
            .Should().Be("configured-secret-0123456789-0123456789");
        Directory.Exists(_dir).Should().BeFalse("a configured key never touches the disk");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheAuthJwtKeyEnvironmentVariableIsReadAsDocumented(bool community)
    {
        // AUTH_JWTKEY is a plain environment variable (the host only maps Auth__JwtKey to Auth:JwtKey).
        JwtKeyProvider.Resolve(Config(("AUTH_JWTKEY", "from-env-secret-0123456789-0123456789")), Env("Production"), community)
            .Should().Be("from-env-secret-0123456789-0123456789");
        Directory.Exists(_dir).Should().BeFalse("a configured key never touches the disk");
    }

    [Fact]
    public void AuthJwtKeyWinsOverTheEnvironmentVariable()
    {
        JwtKeyProvider.Resolve(Config(("Auth:JwtKey", "from-setting-0123456789-0123456789-ab"), ("AUTH_JWTKEY", "from-env-secret-0123456789-0123456789")), Env("Production"), true)
            .Should().Be("from-setting-0123456789-0123456789-ab");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void DevelopmentAndTheTestHostFallBackToTheFixedKey(string environment)
    {
        JwtKeyProvider.Resolve(Config(), Env(environment), isCommunity: false).Should().Be(JwtKeyProvider.DevelopmentKey);
    }

    [Fact]
    public void TheTestExecutionFlagFallsBackToTheFixedKey()
    {
        JwtKeyProvider.Resolve(Config(("TEST_EXECUTION", "true")), Env("Production"), isCommunity: false).Should().Be(JwtKeyProvider.DevelopmentKey);
    }

    [Fact]
    public void CloudWithoutAKeyRefusesToStart()
    {
        var act = () => JwtKeyProvider.Resolve(Config(), Env("Production"), isCommunity: false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Auth:JwtKey is required*");
        Directory.Exists(_dir).Should().BeFalse("cloud must not generate a key");
    }

    [Fact]
    public void CommunityGeneratesARandomKeyOnceAndKeepsIt()
    {
        var env = Env("Production");

        var first = JwtKeyProvider.Resolve(Config(), env, isCommunity: true);
        var second = JwtKeyProvider.Resolve(Config(), env, isCommunity: true);

        first.Should().NotBe(JwtKeyProvider.DevelopmentKey).And.HaveLength(88); // 64 bytes in base64
        second.Should().Be(first, "the key persists across restarts so sessions survive them");
        File.ReadAllText(Path.Combine(_dir, "Data", "jwt.key")).Should().Be(first);
    }

    [Fact]
    public void TwoInstallationsGetDifferentKeys()
    {
        var a = JwtKeyProvider.Resolve(Config(("Auth:JwtKeyPath", Path.Combine(_dir, "a.key"))), Env("Production"), true);
        var b = JwtKeyProvider.Resolve(Config(("Auth:JwtKeyPath", Path.Combine(_dir, "b.key"))), Env("Production"), true);

        a.Should().NotBe(b);
    }

    [Fact]
    public void TheKeyFileIsOnlyReadableByItsOwner()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_dir, "nested", "jwt.key");

        JwtKeyProvider.Resolve(Config(("Auth:JwtKeyPath", path)), Env("Production"), true);

        File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void AnUnusableKeyFileFailsWithAHelpfulMessage()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "jwt.key");
        File.WriteAllText(path, "short");

        var act = () => JwtKeyProvider.Resolve(Config(("Auth:JwtKeyPath", path)), Env("Production"), true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Auth:JwtKey*Auth:JwtKeyPath*");
    }

    [Fact]
    public void TheProviderResolvesLazilyAndOnce()
    {
        var provider = new JwtKeyProvider(Config(("Auth:JwtKeyPath", Path.Combine(_dir, "k"))), Env("Production"));

        provider.GetKey().Should().Be(provider.GetKey());
    }
}
