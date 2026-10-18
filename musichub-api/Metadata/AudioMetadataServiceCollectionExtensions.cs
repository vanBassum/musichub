namespace MusicHub.Api.Metadata;

public static class AudioMetadataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the audio metadata reader behind <see cref="IAudioMetadataReader"/>.
    /// </summary>
    public static IServiceCollection AddAudioMetadata(this IServiceCollection services)
    {
        services.AddSingleton<IAudioMetadataReader, TagLibAudioMetadataReader>();
        return services;
    }
}
