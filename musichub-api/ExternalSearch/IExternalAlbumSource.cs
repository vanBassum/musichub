using System.Text.Json.Serialization;

namespace MusicHub.Api.ExternalSearch;

/// <summary>
/// An album as an external provider names it, the release group that all of its editions belong to: never a catalog
/// record, only provenance plus display text.
/// </summary>
/// <param name="Type">The provider's primary type (album, ...), if it says.</param>
/// <param name="SecondaryTypes">What else the provider says it is: compilation, live, soundtrack, ...</param>
/// <param name="FirstReleaseDate">The provider's own date text (a year, a month or a day), if it has one.</param>
/// <param name="Artists">The album-level artist credit; a Various Artists compilation is credited to that name.</param>
public record ExternalAlbum(
    string Provider,
    string ExternalId,
    string Title,
    string? Disambiguation,
    string Url,
    string? Type,
    IReadOnlyList<string> SecondaryTypes,
    string? FirstReleaseDate,
    IReadOnlyList<ExternalArtistCredit> Artists);

/// <summary>One edition (a concrete release) of an album, as the provider describes it. No track list yet.</summary>
/// <param name="TrackCount">The tracks the provider says the edition has, over all its media; null when it doesn't say.</param>
public record ExternalEdition(
    string Provider,
    string ExternalId,
    string Title,
    string? Disambiguation,
    string Url,
    string? Date,
    string? Country,
    string? Status,
    string? Packaging,
    string? Barcode,
    int? TrackCount);

/// <summary>The editions of one album. <paramref name="Truncated"/> says that the provider has more than were read.</summary>
public record ExternalAlbumEditions(ExternalAlbum Album, IReadOnlyList<ExternalEdition> Editions, bool Truncated);

/// <summary>Whether an edition's track list can be trusted as the provider's full list.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrackListState
{
    /// <summary>Every track the provider says the edition has is present and named.</summary>
    Complete,

    /// <summary>The provider gave some tracks, but fewer than it says exist or some lack a name or an identity.</summary>
    Incomplete,

    /// <summary>The provider has no track list for the edition.</summary>
    Missing,
}

/// <summary>One track of an edition, in the provider's order.</summary>
/// <param name="Position">The 1-based position over the whole edition, across media.</param>
/// <param name="Medium">The 1-based medium (disc) the track is on.</param>
/// <param name="Number">The track number as printed on the medium (may be "A1"), if the provider has it.</param>
/// <param name="Title">Null when the provider gave none; it is never filled in from elsewhere.</param>
/// <param name="RecordingId">The provider's identifier of the recording; null when it gave none.</param>
/// <param name="Artists">The track's own artist credit; empty when the provider gave none.</param>
public record ExternalEditionTrack(
    int Position,
    int Medium,
    string? Number,
    string? Title,
    int? LengthMs,
    string? RecordingId,
    string? RecordingUrl,
    IReadOnlyList<ExternalArtistCredit> Artists);

/// <summary>The ordered track list of one edition, with what is missing from it stated, never papered over.</summary>
/// <param name="AlbumId">The provider's identifier of the album the edition belongs to, if it says.</param>
/// <param name="Problems">In words, what makes the list <see cref="TrackListState.Incomplete"/> or missing.</param>
public record ExternalEditionTrackList(
    ExternalEdition Edition,
    string? AlbumId,
    TrackListState State,
    IReadOnlyList<string> Problems,
    IReadOnlyList<ExternalEditionTrack> Tracks);

/// <summary>
/// A read-only source of existing albums, their editions and each edition's ordered track list. It writes nothing and
/// caches nothing; failures throw <see cref="Identification.CandidateSearchUnavailableException"/>, never an empty
/// result, and an entity the provider doesn't have answers null.
/// </summary>
public interface IExternalAlbumSource
{
    /// <summary>The provider's name; carried on every result as its provenance.</summary>
    string Provider { get; }

    /// <summary>A page of albums: <paramref name="offset"/> results are skipped, then up to <paramref name="limit"/> are read.</summary>
    Task<IReadOnlyList<ExternalAlbum>> SearchAlbumsAsync(string term, int limit, int offset, CancellationToken ct);

    /// <summary>The albums the provider credits to the artist, in the provider's order (no ranking).</summary>
    Task<IReadOnlyList<ExternalAlbum>> GetArtistAlbumsAsync(string artistExternalId, int limit, CancellationToken ct);

    /// <summary>One album with all its editions; null when the provider has no such album.</summary>
    Task<ExternalAlbumEditions?> GetAlbumEditionsAsync(string albumExternalId, CancellationToken ct);

    /// <summary>One edition with its track list; null when the provider has no such edition.</summary>
    Task<ExternalEditionTrackList?> GetEditionTracksAsync(string editionExternalId, CancellationToken ct);
}
