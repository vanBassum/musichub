using MusicHub.Api.Catalog;

namespace MusicHub.Api.Ingestion;

/// <summary>
/// Factual record of a file received by MusicHub. Created before the file is stored;
/// <see cref="Status"/> tracks the upload lifecycle; <see cref="ContentHash"/> and
/// <see cref="SizeBytes"/> are null until the storage write is confirmed.
/// </summary>
public class FileRecord
{
    public int Id { get; set; }
    public required string OriginalFilename { get; set; }
    public required string StorageLocation { get; set; }
    public required string StorageKey { get; set; }
    public FileStatus Status { get; set; }
    public string? ContentHash { get; set; }
    public long? SizeBytes { get; set; }
    public required string Source { get; set; }
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Why the upload is Failed or Expired and what to do next; null otherwise.</summary>
    public string? StatusDetail { get; set; }

    /// <summary>The catalog track this file is an encoding of; null while unidentified. At most one track per file.</summary>
    public int? TrackId { get; set; }
    public Track? Track { get; set; }
}
