using MusicHub.Api.Storage;

namespace MusicHub.Api.Tests.Ingestion;

/// <summary>
/// In-memory IFileStorage that fully reads the stream (so SHA-256 is accumulated) and keeps the bytes.
/// <paramref name="onPut"/> runs before the object is kept, <paramref name="afterPut"/> after it (a PUT that
/// stored the object but reported an error); <paramref name="truncateStoredTo"/> keeps only that many bytes.
/// </summary>
internal sealed class FakeFileStorage(
    Action<string>? onPut = null, Action<string>? afterPut = null, int? truncateStoredTo = null,
    int? failAfterReading = null) : IFileStorage
{
    private readonly Dictionary<string, byte[]> _objects = [];

    public async Task PutAsync(string key, Stream content, string? contentType, CancellationToken ct)
    {
        if (failAfterReading is { } limit)
        {
            // Storage breaks mid-stream: it has consumed part of the body, the body itself never failed.
            var part = new byte[limit];
            await content.ReadExactlyAsync(part, ct);
            throw new InvalidOperationException("S3 connection reset");
        }

        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        onPut?.Invoke(key);
        var bytes = buffer.ToArray();
        _objects[key] = truncateStoredTo is { } n ? bytes[..n] : bytes;
        afterPut?.Invoke(key);
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
