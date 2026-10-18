namespace MusicHub.Api.Identification;

/// <summary>
/// Turns the embedded tags into a candidate. The tags are untrusted evidence: a title and at least one artist are
/// needed for a candidate.
/// </summary>
public sealed class FileMetadataEvidenceProvider : IIdentificationEvidenceProvider
{
    public string Source => "file-metadata";

    public Task<EvidenceReport> CollectAsync(IdentificationContext context, CancellationToken ct)
    {
        var tags = context.Metadata.Tags;
        var artists = tags.Artists.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        if (string.IsNullOrWhiteSpace(tags.Title) || artists.Count == 0)
        {
            return Task.FromResult(EvidenceReport.NothingFound(Source, "The embedded tags have no title and artist."));
        }

        var album = string.IsNullOrWhiteSpace(tags.Album) ? null : tags.Album;
        return Task.FromResult(EvidenceReport.Provided(
            Source,
            [new EvidenceCandidate(tags.Title, artists, album)],
            "Embedded tags (untrusted)."));
    }
}
