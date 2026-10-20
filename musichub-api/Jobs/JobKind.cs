using Hangfire;
using MusicHub.Api.Acquisition;
using MusicHub.Api.Catalog;

namespace MusicHub.Api.Jobs;

/// <summary>
/// A manually startable task the Jobs area shows. <paramref name="SummaryType"/> is what its job returns, so an
/// execution's Hangfire result can be read back; <paramref name="Enqueue"/> enqueues one run and returns its id.
/// </summary>
public sealed record JobKind(
    string Id, string Name, string Description, Type JobType, Type SummaryType, Func<IBackgroundJobClient, string> Enqueue);

public static class JobKinds
{
    public static readonly JobKind CatalogReconciliation = new(
        "catalog-reconciliation",
        "Synchronize catalog",
        "Looks for stored files that are not linked to the catalog and tries to link them.",
        typeof(CatalogReconciliationJob),
        typeof(CatalogReconciliationSummary),
        client => client.Enqueue<CatalogReconciliationJob>(job => job.Run(null, CancellationToken.None)));

    public static readonly JobKind MissingMusicAcquisition = new(
        "missing-music-acquisition",
        "Search for missing music",
        "Searches for music that has no stored file, downloads the best matching MP3 and imports it once it is identified as the right recording.",
        typeof(MissingMusicAcquisitionJob),
        typeof(MissingMusicSummary),
        client => client.Enqueue<MissingMusicAcquisitionJob>(job => job.Run(null, CancellationToken.None)));

    public static readonly IReadOnlyList<JobKind> All = [CatalogReconciliation, MissingMusicAcquisition];

    /// <summary>The kind whose job is <paramref name="jobType"/>; null for any other Hangfire job.</summary>
    public static JobKind? Of(Type? jobType) => All.FirstOrDefault(kind => kind.JobType == jobType);
}
