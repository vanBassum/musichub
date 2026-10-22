using Hangfire;
using MusicHub.Api.Acquisition;
using MusicHub.Api.Catalog;

namespace MusicHub.Api.Jobs;

/// <summary>
/// A manually startable task the Jobs area shows. <paramref name="SummaryType"/> is what its job returns, so an
/// execution's Hangfire result can be read back; <paramref name="Enqueue"/> enqueues one run with the options it was
/// started with and returns its id.
/// </summary>
public sealed record JobKind(
    string Id, string Name, string Description, Type JobType, Type SummaryType,
    Func<IBackgroundJobClient, JobStartOptions, string> Enqueue);

/// <summary>What a manual start chooses for a run. A task ignores the options it has no use for.</summary>
/// <param name="ReIdentifyAll">Synchronize catalog: also identify files that already have a track, recording each attempt and never changing their link.</param>
public sealed record JobStartOptions(bool ReIdentifyAll = false);

public static class JobKinds
{
    public static readonly JobKind CatalogReconciliation = new(
        "catalog-reconciliation",
        "Synchronize catalog",
        "Looks for stored files that are not linked to the catalog and tries to link them.",
        typeof(CatalogReconciliationJob),
        typeof(CatalogReconciliationSummary),
        (client, options) => client.Enqueue<CatalogReconciliationJob>(job => job.Run(null, options.ReIdentifyAll, CancellationToken.None)));

    public static readonly JobKind MissingMusicAcquisition = new(
        "missing-music-acquisition",
        "Search for missing music",
        "Searches for music that has no stored file and queues the best matching MP3 for download. The queue downloads it and it is imported once it is identified as the right recording.",
        typeof(MissingMusicAcquisitionJob),
        typeof(MissingMusicSummary),
        (client, _) => client.Enqueue<MissingMusicAcquisitionJob>(job => job.Run(null, CancellationToken.None)));

    public static readonly IReadOnlyList<JobKind> All = [CatalogReconciliation, MissingMusicAcquisition];

    /// <summary>The kind whose job is <paramref name="jobType"/>; null for any other Hangfire job.</summary>
    public static JobKind? Of(Type? jobType) => All.FirstOrDefault(kind => kind.JobType == jobType);
}
