using MusicHub.Api.Metadata;

namespace MusicHub.Api.Identification;

/// <summary>What an evidence provider gets to work with: the audio, and what the file says about itself.</summary>
/// <param name="Audio">The audio content; positioned at the start, only read, never disposed.</param>
/// <param name="FileNameHint">The file name, used only for its extension; may be null.</param>
/// <param name="Metadata">The metadata read from the file; its tags are untrusted.</param>
public sealed record IdentificationContext(Stream Audio, string? FileNameHint, AudioMetadata Metadata)
{
    /// <summary>
    /// The title and artists the embedded tags name, as a hint. False unless there is a title and at least one
    /// non-blank artist.
    /// </summary>
    public bool TryGetTagHint(out string title, out IReadOnlyList<string> artists)
    {
        var tags = Metadata.Tags;
        artists = tags.Artists.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        title = tags.Title ?? "";
        return !string.IsNullOrWhiteSpace(title) && artists.Count > 0;
    }
}

/// <summary>
/// An independent identification tool. It yields candidates and says what it did; it never decides which identity
/// is right and writes nothing.
/// </summary>
public interface IIdentificationEvidenceProvider
{
    /// <summary>The tool's name, e.g. <c>file-metadata</c>; carried on all its evidence.</summary>
    string Source { get; }

    Task<EvidenceReport> CollectAsync(IdentificationContext context, CancellationToken ct);
}

/// <summary>Whether a provider could do its job, kept apart from what it found.</summary>
public enum EvidenceAvailability
{
    /// <summary>The provider ran and produced at least one candidate.</summary>
    Provided,

    /// <summary>The provider ran and found no candidate.</summary>
    NothingFound,

    /// <summary>The provider could not run, for example because no external service is configured.</summary>
    Unavailable,
}

/// <summary>A recording a tool suggests. A suggestion, not an identity.</summary>
/// <param name="Detail">The tool's own provenance for the suggestion, e.g. an external recording ID and the service's
/// score; shown with the evidence, never read as MusicHub's confidence.</param>
/// <param name="Reference">Where the source names this candidate, when it does.</param>
public sealed record EvidenceCandidate(
    string Title, IReadOnlyList<string> Artists, string? Album, string? Detail = null, EvidenceSourceReference? Reference = null);

/// <summary>A candidate as the source identifies it: its identifiers and URLs, and what the source says about it.</summary>
/// <param name="RecordingId">The source's identifier for the recording.</param>
/// <param name="RecordingUrl">A URL for the recording at the source.</param>
/// <param name="ReleaseId">The source's identifier for the release the candidate is on, if any.</param>
/// <param name="ReleaseUrl">A URL for that release at the source.</param>
/// <param name="Detail">Source-specific evidence, e.g. disambiguation, release status and date, the source's own match score.</param>
public sealed record EvidenceSourceReference(
    string RecordingId, string RecordingUrl, string? ReleaseId, string? ReleaseUrl, string Detail);

/// <summary>One provider's outcome, with the source preserved.</summary>
/// <param name="Source">The provider's name.</param>
/// <param name="Availability">Whether the provider ran and found something.</param>
/// <param name="Candidates">What it found; empty unless <paramref name="Availability"/> is Provided.</param>
/// <param name="Note">What the provider did or why it could not, readable for later evaluation.</param>
public sealed record EvidenceReport(
    string Source,
    EvidenceAvailability Availability,
    IReadOnlyList<EvidenceCandidate> Candidates,
    string Note)
{
    public static EvidenceReport Provided(string source, IReadOnlyList<EvidenceCandidate> candidates, string note) =>
        new(source, EvidenceAvailability.Provided, candidates, note);

    public static EvidenceReport NothingFound(string source, string note) =>
        new(source, EvidenceAvailability.NothingFound, [], note);

    public static EvidenceReport Unavailable(string source, string note) =>
        new(source, EvidenceAvailability.Unavailable, [], note);
}
