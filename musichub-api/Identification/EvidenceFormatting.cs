namespace MusicHub.Api.Identification;

/// <summary>How evidence reports are written into an <see cref="IdentificationResult"/>, shared by the resolvers.</summary>
internal static class EvidenceFormatting
{
    public static IEnumerable<IdentificationEvidence> Describe(EvidenceReport report) => report.Availability switch
    {
        EvidenceAvailability.Provided => report.Candidates.Select(c =>
            new IdentificationEvidence(report.Source, $"{report.Note} Candidate {Format(c)}.")),
        EvidenceAvailability.NothingFound => [new IdentificationEvidence(report.Source, $"Found nothing. {report.Note}")],
        _ => [new IdentificationEvidence(report.Source, $"Unavailable. {report.Note}")],
    };

    public static string Format(EvidenceCandidate c) => $"\"{c.Title}\" by {string.Join(", ", c.Artists)}";
}
