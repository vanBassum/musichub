namespace MusicHub.Api.ExternalSearch;

/// <param name="EditionId">The suggested edition; null when the evidence doesn't single one out.</param>
/// <param name="Reason">Why it is suggested, or why nothing is.</param>
public record EditionSuggestion(string? EditionId, string Reason)
{
    /// <summary>
    /// The product has not decided how to tell an original or standard edition from regional and format editions, so
    /// only the case that needs no rule is suggested: the album has exactly one edition and the provider read all of
    /// it. Anything else suggests nothing, and the user chooses.
    /// </summary>
    public static EditionSuggestion For(ExternalAlbumEditions album) => album switch
    {
        { Editions.Count: 0 } => new(null, "The provider lists no edition."),
        { Truncated: true } => new(null, "The provider has more editions than were read."),
        { Editions.Count: 1 } => new(album.Editions[0].ExternalId, "It is the only edition the provider lists."),
        _ => new(null, "Several editions exist and nothing in the provider's data singles one out as the original."),
    };
}
