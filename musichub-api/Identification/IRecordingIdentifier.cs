namespace MusicHub.Api.Identification;

/// <summary>
/// Proposes which recording an audio file is. Standalone: it writes nothing (no database, no tags, no catalog
/// records) and its contract knows nothing of persistence or the catalog.
/// </summary>
public interface IRecordingIdentifier
{
    /// <summary>
    /// Identifies <paramref name="audio"/>. The stream is only read, and is left open. When identification is not
    /// justified, the result is <see cref="IdentificationStatus.Unresolved"/>, never a guess.
    /// </summary>
    /// <param name="audio">The audio content.</param>
    /// <param name="fileNameHint">The file name, used only for its extension; may be null.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IdentificationResult> IdentifyAsync(Stream audio, string? fileNameHint, CancellationToken ct);
}
