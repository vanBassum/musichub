namespace MusicHub.Api.Identification;

/// <summary>
/// Turns the embedded tags into a candidate. The tags are untrusted: a hint that never establishes identity on its
/// own (the resolver needs a source other than this one to agree).
/// </summary>
public sealed class FileMetadataEvidenceProvider : IIdentificationEvidenceProvider
{
    public const string SourceName = "file-metadata";

    public string Source => SourceName;

    public Task<EvidenceReport> CollectAsync(IdentificationContext context, CancellationToken ct)
    {
        if (!context.TryGetTagHint(out var title, out var artists))
        {
            return Task.FromResult(EvidenceReport.NothingFound(Source, "The embedded tags have no title and artist."));
        }

        var tags = context.Metadata.Tags;
        var album = string.IsNullOrWhiteSpace(tags.Album) ? null : tags.Album;
        return Task.FromResult(EvidenceReport.Provided(
            Source,
            [new EvidenceCandidate(title, artists, album)],
            "Embedded tags (untrusted)."));
    }
}
