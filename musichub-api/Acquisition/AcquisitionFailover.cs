using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Api.Data;

namespace MusicHub.Api.Acquisition;

/// <summary>What <see cref="AcquisitionFailover"/> did for a track.</summary>
public enum FailoverOutcome
{
    /// <summary>The next eligible candidate is queued (or the track already has an active request).</summary>
    Enqueued,

    /// <summary>No candidate is left: a fresh search is scheduled, later.</summary>
    ResearchScheduled,

    /// <summary>No candidate is left and the scheduled searches are used up; the track stays wanted for a manual search.</summary>
    GaveUp,

    /// <summary>The track has a file, or is gone: nothing to acquire.</summary>
    NotNeeded,
}

/// <summary>
/// Keeps one wanted track moving after a source failed (the provider said its peer is offline). Discovery and transfer
/// stay separate processes; this is where they meet. It picks the next eligible candidate through
/// <see cref="TrackAcquisition"/> (quality, version and identity ranking, rejected and offline sources left out) and queues
/// it, and when none is left it schedules a fresh search as a <see cref="PendingResearch"/> with a doubling wait and a
/// bounded number of searches, so nothing loops. The track is never failed for good here: it stays missing. Scoped.
/// </summary>
public sealed class AcquisitionFailover(
    MusicHubDbContext db,
    TrackAcquisition acquisition,
    IDownloadQueueService queue,
    IOptions<DownloadWorkerOptions> options,
    TimeProvider time,
    ILogger<AcquisitionFailover> logger)
{
    /// <summary>The source of a failed download is offline: try the next candidate for its track, or schedule a fresh search.</summary>
    public async Task<FailoverOutcome> FailOverAsync(int trackId, CancellationToken ct)
    {
        var outcome = await TryAcquireAsync(trackId, ct);
        if (outcome is not null)
            return outcome.Value;

        var scheduled = await ScheduleAsync(trackId, "Every known source was offline or used up; searching again later.", ct);
        logger.LogInformation("Track {TrackId} has no eligible candidate left: {Outcome}.", trackId, scheduled);
        return scheduled;
    }

    /// <summary>Runs the scheduled fresh searches that are due and that no other worker claimed first; returns how many ran.</summary>
    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        // Compared in memory: the schedule is a time zone aware value.
        var due = (await db.PendingResearches.AsNoTracking().ToListAsync(ct)).Where(r => r.NextSearchAt <= now).ToList();
        var ran = 0;
        foreach (var research in due)
        {
            if (!await ClaimAsync(research.Id, ct))
                continue;

            ran++;
            var outcome = await TryAcquireAsync(research.TrackId, ct);
            if (outcome is null)
                await ScheduleAsync(research.TrackId, "A scheduled search found no eligible candidate; searching again later.", ct);
        }

        return ran;
    }

    /// <summary>
    /// Searches and queues the best eligible candidate: Enqueued, or NotNeeded when the track has a file or is gone;
    /// null when nothing eligible was found (or the provider could not search). A queued download ends the schedule.
    /// </summary>
    private async Task<FailoverOutcome?> TryAcquireAsync(int trackId, CancellationToken ct)
    {
        var discovery = await acquisition.DiscoverAsync(trackId, ct);
        if (discovery.Stopped is { } stopped)
        {
            if (stopped.Outcome != TrackAcquisitionOutcome.Skipped)
                return null;

            await ClearAsync(trackId, ct);
            return FailoverOutcome.NotNeeded;
        }

        var best = discovery.Fresh[0];
        var enqueued = await queue.EnqueueAsync(new EnqueueDownloadCommand(trackId, best), ct);
        if (enqueued.Outcome == EnqueueOutcome.TrackNotFound)
            return FailoverOutcome.NotNeeded;

        await ClearAsync(trackId, ct);
        logger.LogInformation(
            "Track {TrackId} continues with the next candidate: request {RequestId} ({Outcome}).",
            trackId, enqueued.Request?.Id, enqueued.Outcome);
        return FailoverOutcome.Enqueued;
    }

    /// <summary>Schedules (or pushes back) the track's fresh search, or gives up once the searches are used up.</summary>
    private async Task<FailoverOutcome> ScheduleAsync(int trackId, string reason, CancellationToken ct)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var research = await db.PendingResearches.FirstOrDefaultAsync(r => r.TrackId == trackId, ct);
        if (research is null)
        {
            research = new PendingResearch { TrackId = trackId, CreatedAt = now };
            db.PendingResearches.Add(research);
        }
        else if (research.Searches >= settings.ResearchMaxAttempts)
        {
            db.PendingResearches.Remove(research);
            await SaveAsync(ct);
            return FailoverOutcome.GaveUp;
        }

        var wait = Math.Min(settings.ResearchBaseDelayMinutes * Math.Pow(2, research.Searches), settings.ResearchMaxDelayMinutes);
        research.NextSearchAt = now.AddMinutes(wait);
        research.Reason = AcquisitionHistory.Truncate(reason, PendingResearch.ReasonMaxLength);
        research.UpdatedAt = now;
        await SaveAsync(ct);
        return FailoverOutcome.ResearchScheduled;
    }

    /// <summary>Takes a due search by raising its count; false when another worker did first, or it is gone.</summary>
    private async Task<bool> ClaimAsync(int id, CancellationToken ct)
    {
        var research = await db.PendingResearches.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (research is null)
            return false;

        research.Searches++;
        // Not due again while this claim is being worked on; the claim's result reschedules it or ends it.
        research.NextSearchAt = time.GetUtcNow().AddMinutes(options.Value.ResearchMaxDelayMinutes);
        research.UpdatedAt = time.GetUtcNow();
        return await SaveAsync(ct);
    }

    private async Task ClearAsync(int trackId, CancellationToken ct)
    {
        var research = await db.PendingResearches.FirstOrDefaultAsync(r => r.TrackId == trackId, ct);
        if (research is null)
            return;

        db.PendingResearches.Remove(research);
        await SaveAsync(ct);
    }

    /// <summary>Saves; false when another worker changed or removed the row first (its copy is then forgotten).</summary>
    private async Task<bool> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException e)
        {
            foreach (var entry in e.Entries)
                entry.State = EntityState.Detached;
            return false;
        }
        catch (DbUpdateException)
        {
            // A concurrent failover scheduled the same track first (unique per track): its schedule stands.
            foreach (var entry in db.ChangeTracker.Entries<PendingResearch>().Where(x => x.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            return false;
        }
    }
}
