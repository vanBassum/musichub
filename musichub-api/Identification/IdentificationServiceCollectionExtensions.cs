using Microsoft.Extensions.Options;

namespace MusicHub.Api.Identification;

public static class IdentificationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IRecordingIdentifier"/>: the rule-based or the AI resolver, per the <c>Identification</c>
    /// configuration. The AI strategy without a configured model fails when resolved, it never falls back silently.
    /// Needs <c>IAudioMetadataReader</c> to be registered. The candidate search and the audio fingerprint tools stay
    /// unavailable, and say so in the evidence, until an <see cref="ICandidateSearch"/> or an
    /// <see cref="IAudioFingerprintMatcher"/> is registered.
    /// </summary>
    public static IServiceCollection AddIdentification(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IdentificationOptions>(configuration.GetSection(IdentificationOptions.SectionName));
        services.AddSingleton<IIdentificationEvidenceProvider, FileMetadataEvidenceProvider>();
        services.AddSingleton<IIdentificationEvidenceProvider, CandidateSearchEvidenceProvider>();
        services.AddSingleton<IIdentificationEvidenceProvider, AudioFingerprintEvidenceProvider>();
        services.AddSingleton<IRecordingIdentifier>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<IdentificationOptions>>().Value;
            switch (options.Strategy)
            {
                case IdentificationStrategy.RuleBased:
                    return ActivatorUtilities.CreateInstance<RuleBasedRecordingIdentifier>(sp);
                case IdentificationStrategy.Ai:
                    if (string.IsNullOrWhiteSpace(options.AiModel))
                    {
                        throw new InvalidOperationException(
                            "Identification is not configured: Identification__AiModel is required when Identification__Strategy is Ai.");
                    }
                    return new AiRecordingIdentifier(options.AiModel);
                default:
                    throw new InvalidOperationException($"Unknown identification strategy {options.Strategy}.");
            }
        });
        return services;
    }
}
