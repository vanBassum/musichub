using System.Text.Json.Serialization;

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
/// <param name="Methods">What each identification tool did, for diagnostics; empty when none ran.</param>
public sealed record IdentificationResult(
    IdentificationStatus Status,
    ProposedRecordingIdentity? Identity,
    IReadOnlyList<IdentificationEvidence> Evidence,
    string? UnresolvedReason,
    IReadOnlyList<IdentificationMethodResult>? Methods = null)
{
    public static IdentificationResult Identified(
        ProposedRecordingIdentity identity, IReadOnlyList<IdentificationEvidence> evidence,
        IReadOnlyList<IdentificationMethodResult>? methods = null) =>
        new(IdentificationStatus.Identified, identity, evidence, null, methods);

    public static IdentificationResult Unresolved(
        string reason, IReadOnlyList<IdentificationEvidence>? evidence = null,
        IReadOnlyList<IdentificationMethodResult>? methods = null) =>
        new(IdentificationStatus.Unresolved, null, evidence ?? [], reason, methods);
}

/// <summary>What one identification tool did for a file.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IdentificationMethodStatus
{
    /// <summary>The tool ran and suggested at least one candidate.</summary>
    Found,

    /// <summary>The tool ran and found no candidate.</summary>
    NothingFound,

    /// <summary>The tool is not set up (for example no service or key is configured), so it did not run.</summary>
    Unconfigured,

    /// <summary>The tool tried and failed: not a "no match".</summary>
    Failed,

    /// <summary>The tool was available but the resolver did not use it for this file.</summary>
    NotRun,
}

/// <summary>One tool's result in an identification: its status, its own note, and the candidates it suggested as text.</summary>
public sealed record IdentificationMethodResult(
    string Source, IdentificationMethodStatus Status, string Note, IReadOnlyList<string> Candidates);

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
