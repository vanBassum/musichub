using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Catalog;

/// <summary>
/// One specific recording/performance/version. A remix, live performance or remake is a
/// distinct track. Every track is in the library; it exists independently of audio files, and
/// availability (hence "missing music") is derived from <see cref="Files"/>, not stored.
/// </summary>
public class Track
{
    public int Id { get; set; }
    public required string Title { get; set; }

    /// <summary>Distinguishes versions of the same title, e.g. "Live at Wembley" or "Radio edit".</summary>
    public string? Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<TrackArtist> Artists { get; set; } = [];
    public List<TrackExternalId> ExternalIds { get; set; } = [];
    public List<FileRecord> Files { get; set; } = [];
}
