using Microsoft.Extensions.Options;
using MusicHub.Api.Metadata;

namespace MusicHub.Api.Identification;

public static class IdentificationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IRecordingIdentifier"/>: the rule-based or the AI resolver, per the <c>Identification</c>
    /// configuration. The AI strategy without a configured model fails when resolved, it never falls back silently.
    /// Needs <c>IAudioMetadataReader</c> to be registered. The candidate search and the audio fingerprint tools stay
    /// unavailable, and say so in the evidence, until an <see cref="ICandidateSearch"/> or an
    /// <see cref="IAudioFingerprintMatcher"/> is registered; <c>Identification:MusicBrainz:Enabled</c> registers the
    /// MusicBrainz search, which then needs <c>Contact</c>. The AI strategy also needs an
    /// <see cref="IIdentificationAiClient"/>; without one it leaves every file unresolved.
    /// </summary>
    public static IServiceCollection AddIdentification(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IdentificationOptions>(configuration.GetSection(IdentificationOptions.SectionName));
        var musicBrainz = configuration.GetSection(IdentificationOptions.SectionName).Get<IdentificationOptions>()?.MusicBrainz;
        if (musicBrainz?.Enabled == true)
        {
            services.AddHttpClient(MusicBrainzCandidateSearch.HttpClientName);
            services.AddSingleton<ICandidateSearch>(sp =>
                MusicBrainzCandidateSearch.Create(
                    sp.GetRequiredService<IOptions<IdentificationOptions>>().Value.MusicBrainz,
                    sp.GetRequiredService<IHttpClientFactory>()));
        }
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
                    if (options.AiMaxSteps < 1)
                    {
                        throw new InvalidOperationException("Identification__AiMaxSteps must be at least 1.");
                    }
                    return new AiRecordingIdentifier(
                        options.AiModel,
                        options.AiMaxSteps,
                        sp.GetService<IIdentificationAiClient>(),
                        sp.GetRequiredService<IAudioMetadataReader>(),
                        sp.GetServices<IIdentificationEvidenceProvider>());
                default:
                    throw new InvalidOperationException($"Unknown identification strategy {options.Strategy}.");
            }
        });
        return services;
    }
}
