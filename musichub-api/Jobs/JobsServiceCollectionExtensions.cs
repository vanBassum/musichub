using MusicHub.Api.Catalog;

namespace MusicHub.Api.Jobs;

public static class JobsServiceCollectionExtensions
{
    /// <summary>Hangfire with PostgreSQL storage as the background job engine. It exposes no dashboard; the only HTTP surface is <c>JobsController</c>.</summary>
    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HangfireOptions>(configuration.GetSection(HangfireOptions.SectionName));
        services.AddSingleton<IJobStorageFactory, PostgreSqlJobStorageFactory>();
        services.AddSingleton<JobEngine>();
        services.AddScoped<PingJob>();
        services.AddScoped<PingJobService>();
        services.AddScoped<CatalogReconciliation>();
        services.AddScoped<CatalogReconciliationJob>();
        services.AddScoped<CatalogReconciliationJobService>();
        services.AddHostedService<HangfireServerService>();
        return services;
    }
}
