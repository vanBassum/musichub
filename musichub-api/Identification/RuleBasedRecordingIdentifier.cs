using MusicHub.Api.Metadata;

namespace MusicHub.Api.Identification;

/// <summary>
/// The deterministic resolver; it needs no AI provider. Its only tool for now is the embedded file metadata:
/// a title and at least one artist are required, otherwise the file stays unresolved.
/// </summary>
public sealed class RuleBasedRecordingIdentifier(IAudioMetadataReader metadataReader) : IRecordingIdentifier
{
    private const string FileMetadataSource = "file-metadata";

    public async Task<IdentificationResult> IdentifyAsync(Stream audio, string? fileNameHint, CancellationToken ct)
    {
        AudioMetadata metadata;
        try
        {
            metadata = await metadataReader.ReadAsync(audio, fileNameHint, ct);
        }
        catch (UnsupportedAudioException)
        {
            return IdentificationResult.Unresolved("The file is not readable audio.");
        }

        var tags = metadata.Tags;
        var artists = tags.Artists.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        if (string.IsNullOrWhiteSpace(tags.Title) || artists.Count == 0)
        {
            return IdentificationResult.Unresolved("The embedded tags have no title and artist.");
        }

        // Tags are untrusted, file-provided evidence; the evidence says so.
        var evidence = new IdentificationEvidence(
            FileMetadataSource,
            $"Embedded tags (untrusted) name \"{tags.Title}\" by {string.Join(", ", artists)}.");
        return IdentificationResult.Identified(
            new ProposedRecordingIdentity(tags.Title, artists, string.IsNullOrWhiteSpace(tags.Album) ? null : tags.Album),
            [evidence]);
    }
}
