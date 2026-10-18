namespace MusicHub.Api.Identification;

/// <summary>
/// Matches audio against a fingerprint database. No implementation is registered by default: register one to
/// enable <see cref="AudioFingerprintEvidenceProvider"/>.
/// </summary>
public interface IAudioFingerprintMatcher
{
    /// <summary>The service's name, e.g. its fingerprint database; carried on the evidence.</summary>
    string Source { get; }

    /// <summary>Matches <paramref name="audio"/>, which is only read and left open.</summary>
    Task<IReadOnlyList<EvidenceCandidate>> MatchAsync(Stream audio, string? fileNameHint, CancellationToken ct);
}

/// <summary>Reports itself unavailable when no <see cref="IAudioFingerprintMatcher"/> is registered.</summary>
public sealed class AudioFingerprintEvidenceProvider(IAudioFingerprintMatcher? matcher = null) : IIdentificationEvidenceProvider
{
    public string Source => matcher?.Source ?? "audio-fingerprint";

    public async Task<EvidenceReport> CollectAsync(IdentificationContext context, CancellationToken ct)
    {
        if (matcher is null)
        {
            return EvidenceReport.Unavailable(Source, "No audio fingerprint service is configured.");
        }

        var candidates = await matcher.MatchAsync(context.Audio, context.FileNameHint, ct);
        return candidates.Count == 0
            ? EvidenceReport.NothingFound(Source, "The fingerprint matched no recording.")
            : EvidenceReport.Provided(Source, candidates, "Fingerprint match.");
    }
}
