using System.Security.Cryptography;
using TempooAnalytics.Api.Infrastructure.Modules;

namespace TempooAnalytics.Api.Features.Auth;

/// <summary>The key that signs and validates access and refresh JWTs.</summary>
public interface IJwtKeyProvider
{
    string GetKey();
}

/// <summary>
/// Resolves <c>Auth:JwtKey</c>. No key baked into the code works outside Development:
/// <list type="bullet">
/// <item>A configured key (<c>Auth:JwtKey</c>, env <c>AUTH_JWTKEY</c>/<c>Auth__JwtKey</c>) always wins, in every edition.</item>
/// <item>Development and the test host (<c>TEST_EXECUTION</c> or the <c>Testing</c> environment) fall back to a fixed, public key so a fresh checkout runs.</item>
/// <item>Cloud (billing module loaded) without a key refuses to start.</item>
/// <item>Community without a key generates 64 random bytes once and keeps them in <c>Auth:JwtKeyPath</c>
/// (default <c>{content root}/Data/jwt.key</c>, the folder the GeoLite2 files use, which a self-host mounts as a volume), mode 0600.
/// If the file cannot be created the host refuses to start and says how to fix it. A persistent file beats "fail to start" because a
/// self-host must boot on first run without a manual step, and beats "regenerate on every start" because that signs everybody out on each restart.</item>
/// </list>
/// </summary>
public sealed class JwtKeyProvider(IConfiguration config, IHostEnvironment environment, FeatureModuleCatalog? modules = null) : IJwtKeyProvider
{
    public const string KeySetting = "Auth:JwtKey";
    public const string PathSetting = "Auth:JwtKeyPath";
    /// <summary>The plain environment variable the docs and the error messages name; the host only maps <c>Auth__JwtKey</c> to <c>Auth:JwtKey</c>.</summary>
    public const string EnvironmentVariable = "AUTH_JWTKEY";
    /// <summary>Only ever used in Development and the test host.</summary>
    public const string DevelopmentKey = "dev-tempoo-very-insecure-key-change-me";
    private const int GeneratedKeyBytes = 64;

    private readonly Lazy<string> _key = new(
        () => Resolve(config, environment, Edition.IsCommunity(modules?.Modules ?? [])),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public string GetKey() => _key.Value;

    public static string Resolve(IConfiguration config, IHostEnvironment environment, bool isCommunity)
    {
        var configured = config[KeySetting];
        if (string.IsNullOrWhiteSpace(configured)) configured = config[EnvironmentVariable];
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing") || config["TEST_EXECUTION"] == "true")
            return DevelopmentKey;

        if (!isCommunity)
            throw new InvalidOperationException($"{KeySetting} is required: set it (AUTH_JWTKEY) to a long random secret. The cloud edition does not generate one.");

        var path = config[PathSetting];
        if (string.IsNullOrWhiteSpace(path)) path = Path.Combine(environment.ContentRootPath, "Data", "jwt.key");
        return LoadOrCreate(path);
    }

    private static string LoadOrCreate(string path)
    {
        try
        {
            if (!File.Exists(path)) Create(path);
            var key = File.ReadAllText(path).Trim();
            if (key.Length < 32) throw new InvalidOperationException("the file is empty or shorter than 32 characters");
            return key;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Could not read or create the JWT signing key file '{path}' ({ex.Message}). Either set {KeySetting} (AUTH_JWTKEY) " +
                $"to a long random secret, or point {PathSetting} at a writable, persistent location (in Docker, mount a volume there).", ex);
        }
    }

    private static void Create(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(GeneratedKeyBytes));
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var stream = new FileStream(path, options);
            using var writer = new StreamWriter(stream);
            writer.Write(key);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another replica created it between the check and the write: use theirs.
        }
    }
}
