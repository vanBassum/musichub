namespace MusicHub.Api.Catalog;

/// <summary>
/// A published album or compilation. It carries the release metadata shared by its editions; the tracks belong to an
/// <see cref="AlbumEdition"/>. There is no album artist: each track keeps its own artist attribution.
/// </summary>
public class Album
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<AlbumEdition> Editions { get; set; } = [];
}

/// <summary>
/// One release of an album (standard, deluxe, a remaster, a regional pressing) with its own fixed, ordered track list.
/// Once written an edition and its <see cref="Tracks"/> are never changed: no API edits them, and a different track
/// list is a different edition. It exists independently of audio files.
/// </summary>
public class AlbumEdition
{
    public int Id { get; set; }
    public int AlbumId { get; set; }
    public Album Album { get; set; } = null!;

    /// <summary>Distinguishes the editions of an album, e.g. "Deluxe" or "2011 Remaster".</summary>
    public string? Name { get; set; }

    /// <summary>The release date as the provider gives it, possibly partial: "1973", "1973-03" or "1973-03-01".</summary>
    public string? ReleaseDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public List<AlbumEditionTrack> Tracks { get; set; } = [];
    public List<AlbumEditionExternalId> ExternalIds { get; set; } = [];
}

/// <summary>
/// A position on an edition that refers to a catalog track. It names the exact audio version on that release, so a
/// remaster is a reference to a different track. A track may be on many editions; its files are never copied.
/// </summary>
public class AlbumEditionTrack
{
    public int Id { get; set; }
    public int AlbumEditionId { get; set; }
    public AlbumEdition AlbumEdition { get; set; } = null!;

    /// <summary>1-based place on the edition; unique per edition and the only ordering.</summary>
    public int Position { get; set; }

    public int TrackId { get; set; }
    public Track Track { get; set; } = null!;
}

/// <summary>The provider's own identifier of an edition: the provenance of its metadata and track list.</summary>
public class AlbumEditionExternalId
{
    public int Id { get; set; }
    public int AlbumEditionId { get; set; }
    public AlbumEdition AlbumEdition { get; set; } = null!;
    public required string Provider { get; set; }
    public required string ExternalId { get; set; }
}
