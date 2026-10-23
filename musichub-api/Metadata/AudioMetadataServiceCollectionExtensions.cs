namespace MusicHub.Api.Metadata;

public static class AudioMetadataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the audio metadata reader behind <see cref="IAudioMetadataReader"/> and the cover reader behind
    /// <see cref="IEmbeddedCoverReader"/>.
    /// </summary>
    public static IServiceCollection AddAudioMetadata(this IServiceCollection services)
    {
        services.AddSingleton<IAudioMetadataReader, TagLibAudioMetadataReader>();
        services.AddSingleton<IEmbeddedCoverReader, TagLibEmbeddedCoverReader>();
        return services;
    }
}
