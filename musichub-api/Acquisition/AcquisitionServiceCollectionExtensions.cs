using Microsoft.Extensions.Options;
using MusicHub.Api.Acquisition.Slskd;

namespace MusicHub.Api.Acquisition;

public static class AcquisitionServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AcquisitionHandoff"/>, the missing-music search and, when <c>Slskd__BaseUrl</c> is configured,
    /// slskd as the <see cref="IAcquisitionProvider"/> with <see cref="TrackAcquisition"/> and the
    /// <see cref="DownloadWorker"/> that works off the download queue. Without a provider nothing can acquire.
    /// </summary>
    public static IServiceCollection AddAcquisition(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AcquisitionHandoff>();
        services.AddScoped<AcquisitionHistory>();
        services.AddScoped<IDownloadQueueService, DownloadQueueService>();
        services.Configure<MissingMusicOptions>(configuration.GetSection(MissingMusicOptions.SectionName));
        services.AddScoped<MissingMusicAcquisition>();
        services.Configure<SlskdOptions>(configuration.GetSection(SlskdOptions.SectionName));
        if (string.IsNullOrWhiteSpace(configuration[$"{SlskdOptions.SectionName}:{nameof(SlskdOptions.BaseUrl)}"]))
        {
            return services;
        }

        // Acquiring a track needs a provider, so it exists only with one.
        services.AddScoped<TrackAcquisition>();
        // The queue only gets worked off with a provider: without one requests wait.
        services.Configure<DownloadWorkerOptions>(configuration.GetSection(DownloadWorkerOptions.SectionName));
        services.AddScoped<AcquisitionFailover>();
        services.AddScoped<DownloadProcessor>();
        services.AddScoped<DownloadHandoffProcessor>();
        services.AddHostedService<DownloadWorker>();
        // Each request and each wait applies its own configured timeout, so the client's is switched off.
        services.AddHttpClient<IAcquisitionProvider, SlskdAcquisitionProvider>((sp, client) =>
        {
            var slskd = sp.GetRequiredService<IOptions<SlskdOptions>>().Value;
            slskd.Validate();
            client.BaseAddress = new Uri(slskd.BaseUrl!.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("X-API-Key", slskd.ApiKey);
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        return services;
    }
}
