using MusicHub.Api.Storage;

namespace MusicHub.Api.Tests.Ingestion;

/// <summary>In-memory IFileStorage that fully reads the stream (so SHA-256 is accumulated).</summary>
internal sealed class FakeFileStorage(Action<string>? onPut = null) : IFileStorage
{
    public async Task PutAsync(string key, Stream content, string? contentType, CancellationToken ct)
    {
        await content.CopyToAsync(Stream.Null, ct);
        onPut?.Invoke(key);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream());

    public Task<bool> ExistsAsync(string key, CancellationToken ct) =>
        Task.FromResult(false);

    public Task DeleteAsync(string key, CancellationToken ct) =>
        Task.CompletedTask;
}
