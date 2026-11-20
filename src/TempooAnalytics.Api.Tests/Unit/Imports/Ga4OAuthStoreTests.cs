using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TempooAnalytics.Api.Features.Imports.Ga4;
using TempooAnalytics.Api.Infrastructure.FeatureFlags;
using Xunit;

namespace TempooAnalytics.Api.Tests.Unit.Imports;

public class Ga4OAuthStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly ManualTimeProvider _time = new(Now);
    private readonly RecordingCache _cache = new();
    private readonly Ga4OAuthStore _store;

    public Ga4OAuthStoreTests()
    {
        _store = new Ga4OAuthStore(_cache, new EphemeralDataProtectionProvider(), _time);
    }

    private Ga4ConnectionRecord Connection(DateTimeOffset expiresAt) =>
        new(Guid.NewGuid(), Guid.NewGuid(), FakeGoogleHandler.AccessToken, ["properties/123"], expiresAt);

    [Fact]
    public async Task SavedConnection_IsEncrypted_AndUnderTheExpectedKey()
    {
        await _store.SaveConnectionAsync("conn-1", Connection(Now.AddHours(1)));

        var stored = _cache.Values["ga4:conn:conn-1"];
        Encoding.UTF8.GetString(stored).Should().NotContain(FakeGoogleHandler.AccessToken);
        Encoding.UTF8.GetString(stored).Should().NotContain("properties/123");
    }

    [Fact]
    public async Task Connection_CanOnlyBeReadOnce()
    {
        var record = Connection(Now.AddHours(1));
        await _store.SaveConnectionAsync("conn-1", record);

        var first = await _store.TakeConnectionAsync("conn-1");
        var second = await _store.TakeConnectionAsync("conn-1");

        first!.AccessToken.Should().Be(FakeGoogleHandler.AccessToken);
        first.PropertyIds.Should().Equal("properties/123");
        second.Should().BeNull();
        _cache.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task Connection_ExpiresAfterTenMinutes()
    {
        await _store.SaveConnectionAsync("conn-1", Connection(Now.AddHours(1)));

        _cache.LastOptions!.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromMinutes(10));
        _time.Advance(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1)));
        (await _store.TakeConnectionAsync("conn-1")).Should().BeNull();
    }

    [Fact]
    public async Task Connection_NeverOutlivesTheToken()
    {
        await _store.SaveConnectionAsync("conn-1", Connection(Now.AddMinutes(3)));

        _cache.LastOptions!.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromMinutes(3));
        _time.Advance(TimeSpan.FromMinutes(4));
        (await _store.TakeConnectionAsync("conn-1")).Should().BeNull();
    }

    [Fact]
    public async Task State_IsEncrypted_SingleUse_AndExpires()
    {
        var record = new Ga4StateRecord(Guid.NewGuid(), Guid.NewGuid(), "verifier-secret", Now);
        await _store.SaveStateAsync("state-1", record);
        await _store.SaveStateAsync("state-2", record);

        Encoding.UTF8.GetString(_cache.Values["ga4:state:state-1"]).Should().NotContain("verifier-secret");
        (await _store.TakeStateAsync("state-1")).Should().Be(record);
        (await _store.TakeStateAsync("state-1")).Should().BeNull();

        _time.Advance(TimeSpan.FromMinutes(11));
        (await _store.TakeStateAsync("state-2")).Should().BeNull();
    }

    [Fact]
    public async Task UnknownOrTamperedValues_ReturnNothing()
    {
        (await _store.TakeStateAsync("missing")).Should().BeNull();

        await _cache.SetAsync("ga4:state:forged", Encoding.UTF8.GetBytes("{\"userId\":\"x\"}"), new DistributedCacheEntryOptions());
        (await _store.TakeStateAsync("forged")).Should().BeNull();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void Ga4ImportFlag_IsOffUnlessConfigured(string? value, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FeatureFlags:Ga4Import"] = value })
            .Build();

        new ConfigurationFeatureFlags(configuration).Ga4Import.Should().Be(expected);
    }

    /// <summary>Memory cache that also exposes the raw stored bytes and the last entry options.</summary>
    private sealed class RecordingCache : IDistributedCache
    {
        private readonly MemoryDistributedCache _inner = new(Options.Create(new MemoryDistributedCacheOptions()));
        public Dictionary<string, byte[]> Values { get; } = new();
        public DistributedCacheEntryOptions? LastOptions { get; private set; }

        public byte[]? Get(string key) => _inner.Get(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => _inner.GetAsync(key, token);
        public void Refresh(string key) => _inner.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);
        public void Remove(string key) { Values.Remove(key); _inner.Remove(key); }
        public Task RemoveAsync(string key, CancellationToken token = default) { Values.Remove(key); return _inner.RemoveAsync(key, token); }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { Values[key] = value; LastOptions = options; _inner.Set(key, value, new DistributedCacheEntryOptions()); }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Values[key] = value;
            LastOptions = options;
            // The real expiry is checked by the store against its TimeProvider; the inner cache keeps the value.
            return _inner.SetAsync(key, value, new DistributedCacheEntryOptions(), token);
        }
    }
}
