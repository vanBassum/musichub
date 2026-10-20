namespace MusicHub.Api.Identification;

public enum IdentificationStatus
{
    /// <summary>A recording identity is proposed.</summary>
    Identified,

    /// <summary>Identification is not justified; no identity is proposed.</summary>
    Unresolved,
}

/// <summary>
/// The outcome of identifying one audio file. A proposal, not a catalog fact: it is inspectable through
/// <see cref="Evidence"/> and does not imply that any metadata is overwritten.
/// </summary>
/// <param name="Status">Whether an identity is proposed.</param>
/// <param name="Identity">The proposed identity; null exactly when <paramref name="Status"/> is Unresolved.</param>
/// <param name="Evidence">What the outcome rests on, from the identification tools that were used.</param>
/// <param name="UnresolvedReason">Why nothing is proposed; null when identified.</param>
public sealed record IdentificationResult(
    IdentificationStatus Status,
    ProposedRecordingIdentity? Identity,
    IReadOnlyList<IdentificationEvidence> Evidence,
    string? UnresolvedReason)
{
    public static IdentificationResult Identified(
        ProposedRecordingIdentity identity, IReadOnlyList<IdentificationEvidence> evidence) =>
        new(IdentificationStatus.Identified, identity, evidence, null);

    public static IdentificationResult Unresolved(
        string reason, IReadOnlyList<IdentificationEvidence>? evidence = null) =>
        new(IdentificationStatus.Unresolved, null, evidence ?? [], reason);
}

/// <summary>The identity a resolver proposes for a recording.</summary>
/// <param name="ArtistReferences">The external identifiers a non-tag source gave for the credited artists, where it did.</param>
public sealed record ProposedRecordingIdentity(
    string Title,
    IReadOnlyList<string> Artists,
    string? Album,
    IReadOnlyList<ArtistReference>? ArtistReferences = null);

/// <summary>One piece of evidence: which tool produced it, and what it found.</summary>
/// <param name="Source">The identification tool, e.g. <c>file-metadata</c>.</param>
/// <param name="Description">What the tool found, readable for later evaluation.</param>
public sealed record IdentificationEvidence(string Source, string Description);
