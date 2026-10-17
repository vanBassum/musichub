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

    /// <summary>Returns whether an object exists under <paramref name="key"/>.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);

    /// <summary>Deletes the object under <paramref name="key"/>. Idempotent: deleting a missing key succeeds.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
