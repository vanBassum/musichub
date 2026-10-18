namespace MusicHub.Api.Ingestion;

/// <summary>
/// The received bytes do not match what the client declared (short, long or corrupt). The file
/// record (<see cref="FileId"/>) is marked Failed with <see cref="Reason"/> when that could be persisted.
/// </summary>
public sealed class FileUploadRejectedException(int fileId, string reason) : Exception(reason)
{
    public int FileId { get; } = fileId;
    public string Reason { get; } = reason;
}
