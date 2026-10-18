using MusicHub.Api.Metadata;
using static MusicHub.Api.Identification.EvidenceFormatting;

namespace MusicHub.Api.Identification;

/// <summary>
/// The deterministic resolver; it needs no AI provider. It runs every evidence provider and proposes an identity
/// only when all candidates agree on one recording (same title and artists, ignoring case) and at least one of
/// them comes from a source other than the embedded tags. No candidate, tags alone, or candidates that disagree,
/// leave the file unresolved. All evidence is kept per source either way.
/// </summary>
public sealed class RuleBasedRecordingIdentifier(
    IAudioMetadataReader metadataReader,
    IEnumerable<IIdentificationEvidenceProvider> providers) : IRecordingIdentifier
{
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

        var reports = new List<EvidenceReport>();
        foreach (var provider in providers)
        {
            if (audio.CanSeek)
            {
                audio.Position = 0;
            }
            reports.Add(await provider.CollectAsync(new IdentificationContext(audio, fileNameHint, metadata), ct));
        }

        var evidence = reports.SelectMany(Describe).ToList();
        var candidates = reports.SelectMany(r => r.Candidates.Select(c => (r.Source, Candidate: c))).ToList();
        if (candidates.Count == 0)
        {
            return IdentificationResult.Unresolved("No identification tool produced a candidate.", evidence);
        }

        var identities = candidates.GroupBy(c => IdentityKey(c.Candidate)).ToList();
        if (identities.Count > 1)
        {
            var conflicts = identities.Select(g =>
                $"{Format(g.First().Candidate)} ({string.Join(", ", g.Select(c => c.Source).Distinct())})");
            return IdentificationResult.Unresolved(
                $"The evidence names {identities.Count} different recordings: {string.Join("; ", conflicts)}.",
                evidence);
        }

        if (identities[0].All(c => c.Source == FileMetadataEvidenceProvider.SourceName))
        {
            return IdentificationResult.Unresolved(
                "Only the embedded tags name this recording; tags are hints and need another source to agree.",
                evidence);
        }

        var agreed = identities[0].Select(c => c.Candidate).ToList();
        var first = agreed[0];
        var album = agreed.Select(c => c.Album).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
        return IdentificationResult.Identified(new ProposedRecordingIdentity(first.Title, first.Artists, album), evidence);
    }

    private static string IdentityKey(EvidenceCandidate c) =>
        c.Title.Trim().ToUpperInvariant() + "\u001f" +
        string.Join("\u001e", c.Artists.Select(a => a.Trim().ToUpperInvariant()).Order(StringComparer.Ordinal));
}
