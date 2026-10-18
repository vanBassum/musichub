namespace MusicHub.Api.Identification;

/// <summary>A candidate as the AI sees it. The <paramref name="Id"/> is the only way to propose it.</summary>
public sealed record AiCandidate(string Id, string Title, IReadOnlyList<string> Artists, string? Album);

/// <summary>What one tool that already ran reported, for the AI to reason over.</summary>
public sealed record AiToolOutcome(
    string Tool, EvidenceAvailability Availability, string Note, IReadOnlyList<AiCandidate> Candidates);

/// <summary>What the AI is asked to decide on: the tools it may still run and what the tools run so far found.</summary>
public sealed record AiIdentificationRequest(
    string Model, IReadOnlyList<string> AvailableTools, IReadOnlyList<AiToolOutcome> Outcomes);

public enum AiActionKind
{
    /// <summary>Run the evidence tool named by <see cref="AiIdentificationAction.Tool"/>.</summary>
    RunTool,

    /// <summary>Propose the candidate named by <see cref="AiIdentificationAction.CandidateId"/>.</summary>
    Propose,

    /// <summary>Stop without an identity; <see cref="AiIdentificationAction.Reason"/> says why.</summary>
    GiveUp,
}

/// <summary>The structured action the AI chose. Validated by the resolver, never trusted as is.</summary>
public sealed record AiIdentificationAction(AiActionKind Kind, string? Tool = null, string? CandidateId = null, string? Reason = null);

/// <summary>
/// The seam to an AI model. It turns the evidence so far into the next structured action. Which provider backs it,
/// and how credentials and models are managed, is a separate topic: until an implementation is registered, the AI
/// strategy reports itself unavailable.
/// </summary>
public interface IIdentificationAiClient
{
    Task<AiIdentificationAction> NextActionAsync(AiIdentificationRequest request, CancellationToken ct);
}
