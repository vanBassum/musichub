namespace MusicHub.Api.Storage;

/// <summary>
/// Stores files as opaque objects under a key, in the one configured bucket.
/// Callers choose keys, never the bucket.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Writes <paramref name="content"/> under <paramref name="key"/>, replacing any existing object.
    /// The content is streamed. The service does not dispose <paramref name="content"/>: the caller owns it.
    /// </summary>
    Task PutAsync(string key, Stream content, string? contentType, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the object under <paramref name="key"/> for reading.
    /// The caller owns the returned stream and must dispose it.
    /// </summary>
    /// <exception cref="FileNotFoundException">There is no object under <paramref name="key"/>.</exception>
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Opens bytes <paramref name="start"/> to <paramref name="end"/> (both inclusive, zero-based, within the object)
    /// of the object under <paramref name="key"/>. The caller owns the returned stream and must dispose it.
    /// The default reads from the start and skips; a store that can fetch a range natively overrides it.
    /// </summary>
    /// <exception cref="FileNotFoundException">There is no object under <paramref name="key"/>.</exception>
    async Task<Stream> OpenReadRangeAsync(string key, long start, long end, CancellationToken cancellationToken)
    {
        var content = await OpenReadAsync(key, cancellationToken);
        try
        {
            var skipBuffer = new byte[81920];
            for (var remaining = start; remaining > 0;)
            {
                var read = await content.ReadAsync(skipBuffer.AsMemory(0, (int)Math.Min(remaining, skipBuffer.Length)), cancellationToken);
                if (read == 0)
                    break;
                remaining -= read;
            }
            return new LengthLimitedStream(content, end - start + 1);
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }
    }

    /// <summary>Returns whether an object exists under <paramref name="key"/>.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);

    /// <summary>Deletes the object under <paramref name="key"/>. Idempotent: deleting a missing key succeeds.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
