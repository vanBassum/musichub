using MusicHub.Api.Metadata;
using static MusicHub.Api.Identification.EvidenceFormatting;

namespace MusicHub.Api.Identification;

/// <summary>
/// The AI-driven resolver. A configured model, through <see cref="IIdentificationAiClient"/>, picks which evidence
/// tools to run, and then proposes one of the candidates they produced, or gives up. This class validates every
/// action: the tool must exist and not have run yet, the candidate must be one a tool produced, and the number of
/// steps is capped. Anything invalid, a failing client, or no client at all leaves the file unresolved; it never
/// falls back to guessing. It cannot judge the AI's reasoning, only that the action is well-formed.
/// </summary>
public sealed class AiRecordingIdentifier(
    string model,
    int maxSteps,
    IIdentificationAiClient? client,
    IAudioMetadataReader metadataReader,
    IEnumerable<IIdentificationEvidenceProvider> providers) : IRecordingIdentifier
{
    public string Model { get; } = model;

    public async Task<IdentificationResult> IdentifyAsync(Stream audio, string? fileNameHint, CancellationToken ct)
    {
        if (client is null)
        {
            return IdentificationResult.Unresolved($"No AI provider is available for model \"{Model}\".");
        }

        AudioMetadata metadata;
        try
        {
            metadata = await metadataReader.ReadAsync(audio, fileNameHint, ct);
        }
        catch (UnsupportedAudioException)
        {
            return IdentificationResult.Unresolved("The file is not readable audio.");
        }

        var tools = providers.ToDictionary(p => p.Source, StringComparer.Ordinal);
        var reports = new List<EvidenceReport>();
        var outcomes = new List<AiToolOutcome>();
        var candidates = new Dictionary<string, EvidenceCandidate>(StringComparer.Ordinal);

        for (var step = 0; ; step++)
        {
            var available = tools.Keys.Where(t => outcomes.All(o => o.Tool != t)).ToList();
            AiIdentificationAction? action;
            try
            {
                action = await client.NextActionAsync(new AiIdentificationRequest(Model, available, outcomes.ToList()), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Unresolved($"The AI model \"{Model}\" is unavailable: {ex.Message}", reports);
            }

            switch (action?.Kind)
            {
                case AiActionKind.GiveUp:
                    return Unresolved(
                        string.IsNullOrWhiteSpace(action.Reason) ? "The AI model gave up." : $"The AI model gave up: {action.Reason}",
                        reports);

                case AiActionKind.Propose:
                    if (action.CandidateId is null || !candidates.TryGetValue(action.CandidateId, out var chosen))
                    {
                        return Unresolved($"The AI proposed unknown candidate \"{action.CandidateId}\".", reports);
                    }
                    return IdentificationResult.Identified(
                        new ProposedRecordingIdentity(chosen.Title, chosen.Artists, chosen.Album),
                        reports.SelectMany(Describe).ToList(),
                        Methods(reports));

                case AiActionKind.RunTool:
                    if (step == maxSteps)
                    {
                        return Unresolved($"The AI used its {maxSteps} allowed tool steps without proposing an identity.", reports);
                    }
                    if (action.Tool is null || !available.Contains(action.Tool))
                    {
                        return Unresolved($"The AI asked for unavailable tool \"{action.Tool}\".", reports);
                    }
                    var report = await CollectFromStartAsync(tools[action.Tool], audio, fileNameHint, metadata, ct);
                    reports.Add(report);
                    outcomes.Add(ToOutcome(report, candidates));
                    break;

                default:
                    return Unresolved("The AI returned an invalid action.", reports);
            }
        }
    }

    private static AiToolOutcome ToOutcome(EvidenceReport report, Dictionary<string, EvidenceCandidate> candidates)
    {
        var offered = new List<AiCandidate>();
        foreach (var c in report.Candidates)
        {
            var id = $"c{candidates.Count + 1}";
            candidates[id] = c;
            offered.Add(new AiCandidate(id, c.Title, c.Artists, c.Album));
        }
        return new AiToolOutcome(report.Source, report.Availability, report.Note, offered);
    }

    private IdentificationResult Unresolved(string reason, List<EvidenceReport> reports) =>
        IdentificationResult.Unresolved(reason, reports.SelectMany(Describe).ToList(), Methods(reports));

    // The tools the model used report what they did; the others are listed as not run.
    private List<IdentificationMethodResult> Methods(List<EvidenceReport> reports) =>
        reports.Select(ToMethodResult)
            .Concat(providers.Where(p => reports.All(r => r.Source != p.Source)).Select(p =>
                new IdentificationMethodResult(p.Source, IdentificationMethodStatus.NotRun, "The AI model did not use this tool.", [])))
            .ToList();
}
