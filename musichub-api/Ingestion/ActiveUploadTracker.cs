using System.Collections.Concurrent;

namespace MusicHub.Api.Ingestion;

/// <summary>
/// Knows which uploads are running in this process, by storage key. The stale-upload cleanup never
/// touches a registration whose upload is still running here, however old its creation timestamp is.
/// </summary>
public sealed class ActiveUploadTracker
{
    private readonly ConcurrentDictionary<string, byte> _active = new();

    /// <summary>Marks <paramref name="storageKey"/> as running until the returned scope is disposed.</summary>
    public IDisposable Begin(string storageKey)
    {
        _active[storageKey] = 0;
        return new Scope(_active, storageKey);
    }

    public bool IsActive(string storageKey) => _active.ContainsKey(storageKey);

    private sealed class Scope(ConcurrentDictionary<string, byte> active, string key) : IDisposable
    {
        public void Dispose() => active.TryRemove(key, out _);
    }
}
