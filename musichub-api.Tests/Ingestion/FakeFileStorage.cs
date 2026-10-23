using MusicHub.Api.Storage;

namespace MusicHub.Api.Tests.Ingestion;

/// <summary>In-memory IFileStorage that fully reads the stream (so SHA-256 is accumulated) and keeps the bytes.</summary>
internal sealed class FakeFileStorage(Action<string>? onPut = null) : IFileStorage
{
    private readonly Dictionary<string, byte[]> _objects = [];

    /// <summary>The keys of the objects stored so far.</summary>
    public IReadOnlyCollection<string> Keys => _objects.Keys;

    public async Task PutAsync(string key, Stream content, string? contentType, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        onPut?.Invoke(key);
        _objects[key] = buffer.ToArray();
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct) =>
        _objects.TryGetValue(key, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes))
            : throw new FileNotFoundException($"No stored file under key '{key}'.", key);

    public Task<bool> ExistsAsync(string key, CancellationToken ct) =>
        Task.FromResult(_objects.ContainsKey(key));

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        _objects.Remove(key);
        return Task.CompletedTask;
    }
}
