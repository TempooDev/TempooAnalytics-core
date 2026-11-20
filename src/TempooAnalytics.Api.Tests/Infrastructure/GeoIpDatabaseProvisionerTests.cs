using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TempooAnalytics.Api.Infrastructure;
using Xunit;

namespace TempooAnalytics.Api.Tests.Infrastructure;

public class GeoIpDatabaseProvisionerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "geoip-" + Guid.NewGuid().ToString("N"));

    public GeoIpDatabaseProvisionerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static byte[] Archive(string entryName, byte[] content)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gz, leaveOpen: false))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, entryName) { DataStream = new MemoryStream(content) });
        }
        return ms.ToArray();
    }

    private (GeoIpDatabaseProvisioner Sut, StubHandler Handler, GeoIpDatabase Db) Create(GeoIpOptions options, Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        options.DataDirectory = _dir;
        var handler = new StubHandler(respond ?? (_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var db = new GeoIpDatabase();
        var sut = new GeoIpDatabaseProvisioner(db, Options.Create(options), new StubFactory(handler), NullLogger<GeoIpDatabaseProvisioner>.Instance);
        return (sut, handler, db);
    }

    [Fact]
    public async Task Without_file_and_without_key_it_does_not_download_and_does_not_throw()
    {
        var (sut, handler, db) = Create(new GeoIpOptions());

        await sut.EnsureAsync(CancellationToken.None);

        handler.Requests.Should().BeEmpty();
        db.Reader.Should().BeNull();
    }

    [Fact]
    public async Task With_a_key_and_no_file_it_downloads_with_basic_auth_and_extracts_the_mmdb()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        var (sut, handler, _) = Create(
            new GeoIpOptions { MaxMindAccountId = "12345", MaxMindLicenseKey = "secret" },
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Archive("GeoLite2-Country_20260101/GeoLite2-Country.mmdb", payload)) });

        await sut.EnsureAsync(CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Contain("GeoLite2-Country/download");
        handler.Requests[0].Headers.Authorization!.Scheme.Should().Be("Basic");
        File.ReadAllBytes(Path.Combine(_dir, "GeoLite2-Country.mmdb")).Should().Equal(payload);
    }

    [Fact]
    public async Task A_failed_download_leaves_no_file_and_does_not_throw()
    {
        var (sut, _, db) = Create(
            new GeoIpOptions { MaxMindAccountId = "1", MaxMindLicenseKey = "bad" },
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await sut.EnsureAsync(CancellationToken.None);

        File.Exists(Path.Combine(_dir, "GeoLite2-Country.mmdb")).Should().BeFalse();
        db.Reader.Should().BeNull();
    }

    [Fact]
    public async Task A_fresh_existing_file_is_left_alone()
    {
        var path = Path.Combine(_dir, "GeoLite2-Country.mmdb");
        File.WriteAllBytes(path, [9]);
        var (sut, handler, _) = Create(new GeoIpOptions { MaxMindAccountId = "1", MaxMindLicenseKey = "k" });

        await sut.EnsureAsync(CancellationToken.None);

        handler.Requests.Should().BeEmpty();
        File.ReadAllBytes(path).Should().Equal([9]);
    }

    [Fact]
    public async Task A_stale_file_is_kept_when_the_refresh_fails()
    {
        var path = Path.Combine(_dir, "GeoLite2-Country.mmdb");
        File.WriteAllBytes(path, [9]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-30));
        var (sut, handler, _) = Create(new GeoIpOptions { MaxMindAccountId = "1", MaxMindLicenseKey = "k" });

        await sut.EnsureAsync(CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        File.ReadAllBytes(path).Should().Equal([9]);
    }

    [Fact]
    public void Geolocation_without_a_database_returns_no_country_instead_of_failing()
    {
        var service = new GeolocationService(new GeoIpDatabase(), NullLogger<GeolocationService>.Instance);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("1.2.3.4");

        service.GetCountryCode(context.Request).Should().BeNull();
    }
}
