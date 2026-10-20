using MusicHub.Api.Metadata;

namespace MusicHub.Api.Identification;

/// <summary>How the resolvers run evidence tools and write their reports into an <see cref="IdentificationResult"/>.</summary>
internal static class EvidenceFormatting
{
    public static IEnumerable<IdentificationEvidence> Describe(EvidenceReport report) => report.Availability switch
    {
        EvidenceAvailability.Provided => report.Candidates.Select(c =>
            new IdentificationEvidence(report.Source, $"{report.Note} Candidate {FormatWithReference(c)}.{(c.Detail is null ? "" : $" {c.Detail}")}")),
        EvidenceAvailability.NothingFound => [new IdentificationEvidence(report.Source, $"Found nothing. {report.Note}")],
        EvidenceAvailability.Failed => [new IdentificationEvidence(report.Source, $"Failed. {report.Note}")],
        _ => [new IdentificationEvidence(report.Source, $"Unavailable. {report.Note}")],
    };

    /// <summary>One result per evidence tool, for the diagnostics of a result.</summary>
    public static IdentificationMethodResult ToMethodResult(EvidenceReport report) =>
        new(report.Source, StatusOf(report.Availability), report.Note,
            report.Candidates.Select(c => $"{FormatWithReference(c)}{(c.Detail is null ? "" : $" {c.Detail}")}").ToList());

    private static IdentificationMethodStatus StatusOf(EvidenceAvailability availability) => availability switch
    {
        EvidenceAvailability.Provided => IdentificationMethodStatus.Found,
        EvidenceAvailability.NothingFound => IdentificationMethodStatus.NothingFound,
        EvidenceAvailability.Failed => IdentificationMethodStatus.Failed,
        _ => IdentificationMethodStatus.Unconfigured,
    };

    public static string Format(EvidenceCandidate c) => $"\"{c.Title}\" by {string.Join(", ", c.Artists)}";

    /// <summary>The candidate with what the source says about it: album, URLs and detail.</summary>
    public static string FormatWithReference(EvidenceCandidate c)
    {
        var text = Format(c);
        if (!string.IsNullOrWhiteSpace(c.Album))
        {
            text += $" on \"{c.Album}\"";
        }
        if (c.Reference is { } r)
        {
            text += $" [recording {r.RecordingUrl}";
            text += r.ReleaseUrl is null ? "" : $", release {r.ReleaseUrl}";
            text += string.IsNullOrWhiteSpace(r.Detail) ? "]" : $"; {r.Detail}]";
        }
        return text;
    }

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
