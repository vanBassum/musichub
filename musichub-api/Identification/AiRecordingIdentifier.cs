namespace MusicHub.Api.Identification;

/// <summary>
/// The AI-driven resolver. It refers to a configured model; no AI provider is integrated yet (a separate design
/// topic), so until then it can only report the file as unresolved, never identify it.
/// </summary>
public sealed class AiRecordingIdentifier(string model) : IRecordingIdentifier
{
    public string Model { get; } = model;

    public Task<IdentificationResult> IdentifyAsync(Stream audio, string? fileNameHint, CancellationToken ct) =>
        Task.FromResult(IdentificationResult.Unresolved(
            $"No AI provider is integrated yet for model \"{Model}\"."));
}
