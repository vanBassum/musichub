namespace MusicHub.Api.Ingestion;

/// <summary>
/// Factual record of a file received by MusicHub. Created before the file is stored;
/// <see cref="ContentHash"/> is null until the storage write is confirmed.
/// </summary>
public class FileRecord
{
    public int Id { get; set; }
    public required string OriginalFilename { get; set; }
    public required string StorageLocation { get; set; }
    public required string StorageKey { get; set; }
    public string? ContentHash { get; set; }
    public required string Source { get; set; }
    public DateTimeOffset StartedAt { get; set; }
}
