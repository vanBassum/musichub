using MusicHub.Api.Metadata;

namespace MusicHub.Api.Identification;

/// <summary>How the resolvers run evidence tools and write their reports into an <see cref="IdentificationResult"/>.</summary>
internal static class EvidenceFormatting
{
    public static IEnumerable<IdentificationEvidence> Describe(EvidenceReport report) => report.Availability switch
    {
        EvidenceAvailability.Provided => report.Candidates.Select(c =>
            new IdentificationEvidence(report.Source, $"{report.Note} Candidate {Format(c)}.{(c.Detail is null ? "" : $" {c.Detail}")}")),
        EvidenceAvailability.NothingFound => [new IdentificationEvidence(report.Source, $"Found nothing. {report.Note}")],
        _ => [new IdentificationEvidence(report.Source, $"Unavailable. {report.Note}")],
    };

    public static string Format(EvidenceCandidate c) => $"\"{c.Title}\" by {string.Join(", ", c.Artists)}";

    /// <summary>Runs one tool with the audio stream rewound to the start, so every tool reads the whole file.</summary>
    public static async Task<EvidenceReport> CollectFromStartAsync(
        IIdentificationEvidenceProvider provider, Stream audio, string? fileNameHint, AudioMetadata metadata, CancellationToken ct)
    {
        if (audio.CanSeek)
        {
            audio.Position = 0;
        }
        return await provider.CollectAsync(new IdentificationContext(audio, fileNameHint, metadata), ct);
    }
}
