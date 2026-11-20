using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using MaxMind.GeoIP2;
using Microsoft.Extensions.Options;

namespace TempooAnalytics.Api.Infrastructure;

public sealed class GeoIpOptions
{
    public const string Section = "GeoIp";

    /// <summary>Directory that holds the .mmdb file. Empty = the Data folder next to the app (current behaviour).</summary>
    public string? DataDirectory { get; set; }
    public string Edition { get; set; } = "GeoLite2-Country";
    public string? MaxMindAccountId { get; set; }
    public string? MaxMindLicenseKey { get; set; }
    public string DownloadUrlTemplate { get; set; } = "https://download.maxmind.com/geoip/databases/{0}/download?suffix=tar.gz";
    /// <summary>MaxMind requires GeoLite2 users to keep the database reasonably current; refresh after this age.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(24);

    public bool CanDownload => !string.IsNullOrWhiteSpace(MaxMindAccountId) && !string.IsNullOrWhiteSpace(MaxMindLicenseKey);
}

/// <summary>
/// Makes sure a GeoLite2 country database is available. Existing files are used as they are; when the file is
/// missing (or older than <see cref="GeoIpOptions.MaxAge"/>) and a MaxMind key is configured, it downloads the
/// official archive. Without a file and without a key the API keeps running and simply reports no country.
/// </summary>
public sealed class GeoIpDatabaseProvisioner(
    GeoIpDatabase database,
    IOptions<GeoIpOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<GeoIpDatabaseProvisioner> logger) : BackgroundService
{
    private readonly GeoIpOptions _options = options.Value;

    public string DatabasePath => Path.Combine(_options.DataDirectory!, _options.Edition + ".mmdb");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await EnsureAsync(stoppingToken);
            try { await Task.Delay(_options.CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One pass: load what exists, download when needed. Never throws.</summary>
    public async Task EnsureAsync(CancellationToken ct)
    {
        try
        {
            var path = DatabasePath;
            var exists = File.Exists(path);
            if (exists && database.Reader is null) TryLoad(path);

            var stale = exists && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > _options.MaxAge;
            if (!exists || stale)
            {
                if (_options.CanDownload)
                {
                    if (await DownloadAsync(path, ct)) TryLoad(path);
                }
                else if (!exists)
                {
                    logger.LogWarning(
                        "GeoLite2 database not found at {Path} and no MaxMind credentials are configured (GeoIp:MaxMindAccountId / GeoIp:MaxMindLicenseKey). " +
                        "Country lookup by IP is disabled; set both to download it.", path);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "GeoIP database provisioning failed; keeping what is loaded.");
        }
    }

    private void TryLoad(string path)
    {
        try
        {
            database.Set(new DatabaseReader(path, MaxMind.Db.FileAccessMode.MemoryMapped));
            logger.LogInformation("Loaded GeoIP database {Path}", path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not open GeoIP database {Path}", path);
        }
    }

    private async Task<bool> DownloadAsync(string destination, CancellationToken ct)
    {
        var url = string.Format(_options.DownloadUrlTemplate, _options.Edition);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.MaxMindAccountId}:{_options.MaxMindLicenseKey}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        var client = httpClientFactory.CreateClient(nameof(GeoIpDatabaseProvisioner));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Never log the key; the status is enough to tell a bad key (401) from a quota problem (429).
            logger.LogWarning("MaxMind download for {Edition} failed with HTTP {Status}", _options.Edition, (int)response.StatusCode);
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".download";
        await using (var body = await response.Content.ReadAsStreamAsync(ct))
        await using (var gzip = new GZipStream(body, CompressionMode.Decompress))
        await using (var tar = new TarReader(gzip))
        {
            TarEntry? entry;
            var found = false;
            while ((entry = await tar.GetNextEntryAsync(copyData: false, ct)) is not null)
            {
                if (entry.EntryType != TarEntryType.RegularFile || !entry.Name.EndsWith(".mmdb", StringComparison.OrdinalIgnoreCase)) continue;
                await using var output = File.Create(temp);
                await entry.DataStream!.CopyToAsync(output, ct);
                found = true;
                break;
            }
            if (!found)
            {
                logger.LogWarning("MaxMind archive for {Edition} contained no .mmdb file", _options.Edition);
                return false;
            }
        }

        File.Move(temp, destination, overwrite: true);
        logger.LogInformation("Downloaded GeoIP database {Edition} to {Path}", _options.Edition, destination);
        return true;
    }
}
