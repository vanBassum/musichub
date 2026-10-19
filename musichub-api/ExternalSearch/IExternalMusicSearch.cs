using MusicHub.Api.Identification;

namespace MusicHub.Api.ExternalSearch;

/// <param name="ExternalId">The provider's own stable identifier of the artist, if the answer carried one.</param>
public record ExternalArtistCredit(string Name, string? ExternalId);

/// <summary>A track as an external provider names it: never a catalog record, only provenance plus display text.</summary>
public record ExternalTrack(
    string Provider,
    string ExternalId,
    string Title,
    string? Disambiguation,
    string Url,
    IReadOnlyList<ExternalArtistCredit> Artists);

/// <summary>An artist as an external provider names it: never a catalog record.</summary>
public record ExternalArtist(
    string Provider,
    string ExternalId,
    string Name,
    string? Disambiguation,
    string Url);

/// <summary>The text fields a track search may look in. Providers apply the ones they support.</summary>
public record ExternalSearchFields(bool Title, bool Artist);

/// <summary>
/// A read-only search of an external music metadata provider. It writes nothing and caches nothing; failures throw
/// <see cref="CandidateSearchUnavailableException"/>, never an empty result. No implementation is registered unless
/// a provider is enabled.
/// </summary>
public interface IExternalMusicSearch
{
    /// <summary>The provider's name; carried on every result as its provenance.</summary>
    string Provider { get; }

    Task<IReadOnlyList<ExternalTrack>> SearchTracksAsync(
        string term, ExternalSearchFields fields, int limit, CancellationToken ct);

    Task<IReadOnlyList<ExternalArtist>> SearchArtistsAsync(string term, int limit, CancellationToken ct);
}
