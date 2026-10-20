namespace MusicHub.Api.Catalog;

/// <param name="Status"><c>Linked</c>, or <c>Unlinked</c> for a correction by a person.</param>
/// <param name="Origin">Who set the status: <c>Automatic</c> or <c>Manual</c>.</param>
/// <param name="Evidence">What an automatic link rests on; null for a manual link.</param>
/// <param name="Url">Where the source shows the artist.</param>
public record ArtistExternalLinkResponse(
    int Id,
    string Source,
    string ExternalId,
    string Url,
    string Status,
    string Origin,
    string? Evidence,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ArtistExternalLinkResponse From(ArtistExternalLink l) => new(
        l.Id, l.Source, l.ExternalId, ExternalSources.ArtistUrl(l.Source, l.ExternalId),
        l.Status.ToString(), l.Origin.ToString(), l.Evidence, l.CreatedAt, l.UpdatedAt);
}

public record ArtistExternalLinkListResponse(List<ArtistExternalLinkResponse> Items);

public record LinkArtistExternalIdRequest(string? Source, string? ExternalId);
