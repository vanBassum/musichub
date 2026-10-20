using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Acquisition;

/// <summary>
/// The two hashes of one downloaded file, kept apart on purpose: <see cref="ContentHash"/> is the identity of the exact
/// bytes (tags included), <see cref="Audio"/> the identity of the encoded MP3 audio (see <see cref="Mp3AudioPayload"/>).
/// Neither is an acoustic fingerprint or says which recording the file is.
/// </summary>
public sealed record DownloadHashes(string ContentHash, AudioHashResult Audio)
{
    /// <summary>Reads the file once, whatever the audio parse says, so the full-file hash always covers every byte.</summary>
    public static async Task<DownloadHashes> ComputeAsync(IAcquiredFile file, CancellationToken ct)
    {
        await using var content = file.OpenRead();
        await using var hashing = new Sha256HashingStream(content);
        var audio = await Mp3AudioPayload.HashAsync(hashing, ct);
        await hashing.CopyToAsync(Stream.Null, ct);
        return new DownloadHashes(hashing.GetHashHex(), audio);
    }
}
