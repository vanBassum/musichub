namespace MusicHub.Api.Metadata;

/// <summary>
/// Reads technical properties and embedded tags from audio. Tags are untrusted, file-provided evidence,
/// not MusicHub's recording identity.
/// </summary>
public interface IAudioMetadataReader
{
    /// <summary>
    /// Reads the metadata of <paramref name="audio"/>. The stream is only read, and is left open.
    /// </summary>
    /// <param name="audio">The audio content.</param>
    /// <param name="fileNameHint">The file name, used only for its extension to pick the format; may be null.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="UnsupportedAudioException">The input is empty, corrupt or not a supported audio format.</exception>
    Task<AudioMetadata> ReadAsync(Stream audio, string? fileNameHint, CancellationToken ct);
}
