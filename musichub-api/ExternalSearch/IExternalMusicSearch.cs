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
/// <param name="Type">What kind of artist the provider says it is (person, group, ...), if it says.</param>
/// <param name="Area">Where the provider places the artist, if it does.</param>
public record ExternalArtist(
    string Provider,
    string ExternalId,
    string Name,
    string? Disambiguation,
    string Url,
    string? Type = null,
    string? Area = null);

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

    /// <summary>One artist by the provider's own identifier; null when the provider has no such artist.</summary>
    Task<ExternalArtist?> GetArtistAsync(string externalId, CancellationToken ct);

    /// <summary>
    /// Recordings the provider credits to the artist, in the provider's order. That order is not a ranking: the
    /// provider supplies no popularity data.
    /// </summary>
    Task<IReadOnlyList<ExternalTrack>> GetArtistTracksAsync(string externalId, int limit, CancellationToken ct);
}
