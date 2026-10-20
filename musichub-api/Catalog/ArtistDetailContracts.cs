namespace MusicHub.Api.Catalog;

/// <param name="Url">Where the source shows the artist.</param>
public record LinkedArtistSource(string Source, string ExternalId, string Url);

/// <param name="Sources">The external identifiers currently linked to the artist (not the ones a person unlinked).</param>
public record ArtistDetailResponse(int Id, string Name, int TrackCount, List<LinkedArtistSource> Sources);

/// <summary>The catalog artist an external identifier is linked to.</summary>
public record LinkedArtistResponse(int ArtistId);

public record ArtistTrackListResponse(List<TrackResponse> Items, int Total);
