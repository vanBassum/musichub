namespace MusicHub.Api.Ingestion;

/// <summary>Why the received bytes do not match what the client declared.</summary>
public enum UploadIntegrityFailure
{
    /// <summary>Fewer bytes than declared arrived: a truncated transfer.</summary>
    Incomplete,

    /// <summary>More bytes than declared arrived, or the full length arrived with a different SHA-256.</summary>
    Corrupt,
}

/// <summary>
/// The received bytes failed verification against the client's declared length and SHA-256.
/// The file record (<see cref="FileId"/>) is marked Failed when that could be persisted.
/// </summary>
public sealed class UploadIntegrityException(int fileId, UploadIntegrityFailure failure)
    : Exception($"The upload failed verification: {failure}.")
{
    public int FileId { get; } = fileId;
    public UploadIntegrityFailure Failure { get; } = failure;
}
