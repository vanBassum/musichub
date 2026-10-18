namespace MusicHub.Api.Ingestion;

/// <summary>
/// The received bytes do not match what the client declared (short, long or corrupt). The file
/// record (<see cref="FileId"/>) is marked Failed with the machine-readable <see cref="Reason"/>
/// when that could be persisted; <see cref="Detail"/> is a safe sentence for the client.
/// </summary>
public sealed class FileUploadRejectedException(int fileId, string reason, string detail) : Exception(detail)
{
    public int FileId { get; } = fileId;
    public string Reason { get; } = reason;
    public string Detail { get; } = detail;
}
