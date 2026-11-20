using MaxMind.GeoIP2;

namespace TempooAnalytics.Api.Infrastructure;

/// <summary>
/// Holds the current GeoLite2 country reader. The reader is null while no database is available
/// (self-host without a MaxMind key); it is swapped when the provisioner downloads a fresh file.
/// </summary>
public sealed class GeoIpDatabase : IDisposable
{
    private IGeoIP2DatabaseReader? _reader;

    public IGeoIP2DatabaseReader? Reader => Volatile.Read(ref _reader);

    public void Set(IGeoIP2DatabaseReader? reader)
    {
        var old = Interlocked.Exchange(ref _reader, reader);
        // Readers in flight may still be using the old one for a moment; let the GC release the mapping.
        if (old is IDisposable d && !ReferenceEquals(old, reader))
        {
            _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => d.Dispose());
        }
    }

    public void Dispose() => (Interlocked.Exchange(ref _reader, null) as IDisposable)?.Dispose();
}
