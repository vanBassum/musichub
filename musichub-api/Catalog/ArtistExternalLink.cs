namespace MusicHub.Api.Catalog;

public enum ExternalLinkStatus
{
    /// <summary>The identifier belongs to the artist.</summary>
    Linked,

    /// <summary>A person said the identifier does not belong to the artist; automation never links the pair again.</summary>
    Unlinked,
}

/// <summary>Who made the current decision about the link.</summary>
public enum ExternalLinkOrigin
{
    Automatic,
    Manual,
}

/// <summary>
/// The durable relation between an internal <see cref="Artist"/> and the identifier a source (provider) has for it.
/// The internal artist id stays the canonical identity; a source's identifier is only ever one of its links. An
/// identifier is <see cref="ExternalLinkStatus.Linked"/> to at most one artist per source. Unlinking keeps the row as
/// <see cref="ExternalLinkStatus.Unlinked"/>, so a correction survives later automatic matching.
/// </summary>
public class ArtistExternalLink
{
    public int Id { get; set; }
    public int ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;

    /// <summary>The provider, normalized (lower case), e.g. <c>musicbrainz</c>.</summary>
    public required string Source { get; set; }

    /// <summary>The provider's identifier of the artist, normalized per source.</summary>
    public required string ExternalId { get; set; }

    public ExternalLinkStatus Status { get; set; }

    /// <summary>Who set the current <see cref="Status"/>.</summary>
    public ExternalLinkOrigin Origin { get; set; }

    /// <summary>What an automatic link rests on; kept when a person later unlinks it. Null for a manual link.</summary>
    public string? Evidence { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When <see cref="Status"/> or <see cref="Origin"/> last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
