using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;

namespace MusicHub.Api.Acquisition;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrackAcquisitionOutcome
{
    /// <summary>Nothing worth downloading was offered (no MP3 that claims this recording, or only candidates rejected before).</summary>
    NothingFound,

    /// <summary>The best candidate was put in the download queue; the transfer happens later, outside the run.</summary>
    Enqueued,

    /// <summary>The track, or its best candidate, already has an active queued request; nothing was added.</summary>
    AlreadyQueued,

    /// <summary>The provider could not search, so nothing is known about this track.</summary>
    ProviderUnavailable,

    /// <summary>The track had a file, or was gone, when its turn came.</summary>
    Skipped,

    /// <summary>Processing the track failed unexpectedly.</summary>
    Failed,
}

/// <summary>What a run did for one track.</summary>
public sealed record TrackAcquisitionResult(
    int TrackId, string Title, string Artists, TrackAcquisitionOutcome Outcome, string Detail);

/// <summary>
/// What searching for one track found. <paramref name="Stopped"/> is set when there is nothing to download; otherwise
/// <paramref name="Fresh"/> holds the candidates worth downloading, best first.
/// </summary>
public sealed record TrackDiscovery(
    int TrackId, Track? Track, WantedRecording? Wanted, IReadOnlyList<AcquisitionCandidate> Fresh, TrackAcquisitionResult? Stopped,
    Guid? SearchId = null)
{
    public static TrackDiscovery Ended(int trackId, TrackAcquisitionResult result) => new(trackId, null, null, [], result);

    public TrackAcquisitionResult Result(TrackAcquisitionOutcome outcome, string detail) =>
        new(TrackId, Track!.Title, string.Join(", ", Wanted!.Artists), outcome, detail);
}

/// <summary>
/// Searches for one missing track and ranks what is offered, without downloading anything. A candidate rejected before
/// (<see cref="AcquisitionHistory"/>) is left out, so it is not downloaded again.
/// </summary>
public sealed class TrackAcquisition(
    MusicHubDbContext db,
    AcquisitionHistory history,
    TrackActivityLog activity,
    IAcquisitionProvider provider,
    IOptions<DownloadWorkerOptions> options,
    TimeProvider time)
{
    /// <summary>
    /// Searches for the track and ranks what is offered, without downloading anything. <see cref="TrackDiscovery.Stopped"/>
    /// is the result when there is nothing to download; otherwise <see cref="TrackDiscovery.Fresh"/> lists the plausible
    /// candidates not rejected before, best first. Each search writes its start and its outcome, with the counts, to the
    /// track's activity timeline under one <see cref="TrackDiscovery.SearchId"/>.
    /// </summary>
    public async Task<TrackDiscovery> DiscoverAsync(
        int trackId, CancellationToken cancellationToken, string? jobExecutionId = null)
    {
        var track = await db.Tracks.AsNoTracking()
            .Include(t => t.Artists).ThenInclude(a => a.Artist)
            .FirstOrDefaultAsync(t => t.Id == trackId, cancellationToken);
        if (track is null || await HasStoredAudioAsync(trackId, cancellationToken))
            return TrackDiscovery.Ended(trackId, new TrackAcquisitionResult(
                trackId, track?.Title ?? "", "", TrackAcquisitionOutcome.Skipped,
                "The track was gone or had a stored file when its turn came."));

        var wanted = WantedRecording.Of(track);
        TrackDiscovery Ended(TrackAcquisitionOutcome outcome, string detail) =>
            TrackDiscovery.Ended(trackId, new TrackAcquisitionResult(
                trackId, track.Title, string.Join(", ", wanted.Artists), outcome, detail));

        var searchId = Guid.NewGuid();
        Task Record(TrackActivityKind kind, string summary, string? detail = null, TrackActivityLinks? counts = null) =>
            activity.RecordAsync(trackId, kind, summary, cancellationToken, detail,
                (counts ?? new TrackActivityLinks()) with { SearchId = searchId, JobExecutionId = jobExecutionId });

        await Record(TrackActivityKind.SearchStarted, "Search started.");
        IReadOnlyList<AcquisitionCandidate> offered;
        try
        {
            offered = await provider.SearchAsync(new AcquisitionQuery(string.Join(' ', wanted.Artists), wanted.Title), cancellationToken);
        }
        catch (AcquisitionUnavailableException e)
        {
            await Record(TrackActivityKind.SearchFailed, "The provider could not search; nothing is known about this track.", e.Message);
            return Ended(TrackAcquisitionOutcome.ProviderUnavailable, e.Message);
        }

        var plausible = CandidateRanking.Rank(wanted, offered);
        // A candidate rejected before is not downloaded again. A failed or cancelled transfer judged nothing, so it may be
        // tried again.
        // A source confirmed offline is skipped for the cooldown, whatever file it offers.
        var offlineSince = time.GetUtcNow().AddMinutes(-options.Value.SourceOfflineCooldownMinutes);
        var fresh = new List<AcquisitionCandidate>();
        var offline = 0;
        foreach (var candidate in plausible)
        {
            if (await history.WasSourceOfflineSinceAsync(candidate, offlineSince, cancellationToken))
                offline++;
            else if (!await history.WasRejectedAsync(candidate, cancellationToken))
                fresh.Add(candidate);
        }
        var counts = $"{offered.Count} results, {plausible.Count} plausible MP3s, {fresh.Count} eligible.";
        if (fresh.Count == 0)
        {
            await Record(TrackActivityKind.NotFound, $"Nothing to download: {counts}", null,
                new TrackActivityLinks { ResultCount = offered.Count, PlausibleCount = plausible.Count, EligibleCount = 0 });
            return Ended(TrackAcquisitionOutcome.NothingFound,
                $"{offered.Count} results, {plausible.Count} plausible MP3s, {offline} from sources offline, " +
                $"{plausible.Count - offline - fresh.Count} rejected earlier.");
        }

        await Record(TrackActivityKind.SearchCompleted, $"Search finished: {counts}", null,
            new TrackActivityLinks { ResultCount = offered.Count, PlausibleCount = plausible.Count, EligibleCount = fresh.Count });
        return new TrackDiscovery(trackId, track, wanted, fresh, null, searchId);
    }

    private Task<bool> HasStoredAudioAsync(int trackId, CancellationToken cancellationToken) =>
        db.Files.Where(f => f.TrackId == trackId).AnyAsync(TrackResponse.IsStoredAudio, cancellationToken);
}
