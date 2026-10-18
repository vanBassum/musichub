namespace MusicHub.Api.Ingestion;

/// <summary>
/// A handled upload/storage failure. The file record (<see cref="FileId"/>) exists and is
/// marked Failed when that could be persisted; the original error is the inner exception.
/// </summary>
public sealed class FileIngestionException(int fileId, Exception inner)
    : Exception("The file could not be stored.", inner)
{
    public int FileId { get; } = fileId;
}
