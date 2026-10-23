using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Data;

namespace MusicHub.Api.Acquisition;

/// <summary>
/// The one owner of the durable download queue (see <see cref="IDownloadQueueService"/>). Races are settled by the
/// database, not by locks: two filtered unique indexes allow one active request per track and per candidate, and
/// <see cref="DownloadRequest.Status"/> is a concurrency token, so a transition applies only to the status it was read
/// in. Every transition writes its event to the track's activity timeline (<see cref="TrackActivityLog"/>) in the same save,
/// so the timeline shows exactly the transitions that happened. Scoped: one instance per unit of work, like the context it
/// uses.
/// </summary>
public class DownloadQueueService(MusicHubDbContext db, TimeProvider time, TrackActivityLog activity) : IDownloadQueueService
{
    public async Task<EnqueueResult> EnqueueAsync(EnqueueDownloadCommand command, CancellationToken ct)
    {
        if (!await db.Tracks.AnyAsync(t => t.Id == command.TrackId, ct))
            return new EnqueueResult(EnqueueOutcome.TrackNotFound, null);

        var identity = AcquisitionHistory.StoredIdentity(command.Candidate);
        var existing = await FindActiveAsync(command.TrackId, identity, ct);
        if (existing is not null)
            return await AlreadyActiveAsync(existing.Id, ct);

        var now = time.GetUtcNow();
        var request = new DownloadRequest
        {
            TrackId = command.TrackId,
            Provider = identity.Provider,
            Source = identity.Source,
            RemotePath = identity.RemotePath,
            SizeBytes = command.Candidate.SizeBytes,
            BitrateKbps = command.Candidate.BitrateKbps,
            Format = command.Candidate.Format is { } format
                ? AcquisitionHistory.Truncate(format, DownloadRequest.FormatMaxLength) : null,
            JobExecutionId = command.JobExecutionId is { } job
                ? AcquisitionHistory.Truncate(job, AcquisitionDownload.JobExecutionIdMaxLength) : null,
            Status = DownloadRequestStatus.Queued,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.DownloadRequests.Add(request);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent enqueue won the unique index between our check and our insert.
            db.Entry(request).State = EntityState.Detached;
            var winner = await FindActiveAsync(command.TrackId, identity, ct);
            if (winner is null)
                throw;
            return await AlreadyActiveAsync(winner.Id, ct);
        }

        await activity.RecordAsync(
            command.TrackId, TrackActivityKind.DownloadQueued, "Download queued.", ct,
            links: new TrackActivityLinks
            {
                SearchId = command.SearchId, DownloadRequestId = request.Id, JobExecutionId = request.JobExecutionId,
            });
        return new EnqueueResult(EnqueueOutcome.Created, await GetAsync(request.Id, ct));
    }

    public async Task<DownloadRequestView?> GetAsync(int id, CancellationToken ct)
    {
        var request = await Viewable().Where(r => r.Id == id).FirstOrDefaultAsync(ct);
        return request is null ? null : ToView(request);
    }

    public async Task<DownloadRequestPage> ListAsync(
        DownloadRequestStatus? status, int page, int pageSize, CancellationToken ct)
    {
        var query = Viewable();
        if (status is { } chosen)
            query = query.Where(r => r.Status == chosen);
        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return new DownloadRequestPage(rows.Select(ToView).ToList(), total, page, pageSize);
    }

    public async Task<RetryResult> RetryAsync(int id, CancellationToken ct)
    {
        var request = await FindVisibleAsync(id, ct);
        if (request is null)
            return new RetryResult(RetryOutcome.NotFound, null);

        if (request.Status != DownloadRequestStatus.Failed)
            return new RetryResult(RetryOutcome.NotFailed, await GetAsync(id, ct));

        try
        {
            if (!await TransitionAsync(request, DownloadRequestStatus.Queued, r =>
                {
                    r.AttemptCount = 0;
                    r.BytesTransferred = 0;
                    r.NextAttemptAt = null;
                    r.FinishedAt = null;
                    r.Failure = null;
                    r.Error = null;
                }, ct, _ => new ActivityNote(TrackActivityKind.DownloadRetriedManually, "Download queued again by the user.")))
                return new RetryResult(RetryOutcome.NotFailed, await GetAsync(id, ct));
        }
        catch (DbUpdateException)
        {
            // The unique indexes refused a second active request for the track or the candidate.
            db.Entry(request).State = EntityState.Detached;
            return new RetryResult(RetryOutcome.AlreadyActive, await GetAsync(id, ct));
        }

        return new RetryResult(RetryOutcome.Retried, await GetAsync(id, ct));
    }

    public async Task<RemoveOutcome> RemoveAsync(int id, CancellationToken ct)
    {
        var request = await FindVisibleAsync(id, ct);
        if (request is null)
            return RemoveOutcome.NotFound;
        if (!DownloadRequestStatuses.Removable.Contains(request.Status))
            return RemoveOutcome.NotRemovable;

        var now = time.GetUtcNow();
        request.RemovedAt = now;
        request.UpdatedAt = now;
        // The status is the concurrency token: a retry that won the race makes this save fail, and nothing is removed.
        return await SaveAsync(request, ct) ? RemoveOutcome.Removed : RemoveOutcome.NotRemovable;
    }

    public async Task<ClaimedDownload?> ClaimNextAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        // Compared in memory: only queued requests are loaded, and the schedule is a time zone aware value.
        var queued = (await db.DownloadRequests.Where(r => r.Status == DownloadRequestStatus.Queued)
                .OrderBy(r => r.Id).ToListAsync(ct))
            .Where(r => r.NextAttemptAt is null || r.NextAttemptAt <= now);
        foreach (var request in queued)
        {
            // Losing the race for one request means another worker has it: try the next.
            if (!await TransitionAsync(request, DownloadRequestStatus.Downloading, r =>
                {
                    r.AttemptCount++;
                    r.StartedAt ??= now;
                    r.NextAttemptAt = null;
                    r.BytesTransferred = 0;
                }, ct, r => new ActivityNote(TrackActivityKind.DownloadStarted, $"Download attempt {r.AttemptCount} started.")))
                continue;

            var candidate = new AcquisitionCandidate(
                request.Provider, request.Source, request.RemotePath, request.SizeBytes ?? 0,
                request.Format, request.BitrateKbps);
            return new ClaimedDownload(request.Id, request.TrackId, candidate, request.AttemptCount);
        }

        return null;
    }

    public async Task<bool> ReportProgressAsync(int id, long bytesTransferred, CancellationToken ct)
    {
        var request = await FindAsync(id, ct);
        if (request is not { Status: DownloadRequestStatus.Downloading })
            return false;

        request.BytesTransferred = Math.Max(bytesTransferred, 0);
        request.UpdatedAt = time.GetUtcNow();
        return await SaveAsync(request, ct);
    }

    public Task<bool> MarkDownloadedAsync(int id, int? attemptId, CancellationToken ct) =>
        TransitionAsync(id, DownloadRequestStatus.Downloaded, r =>
        {
            r.AcquisitionDownloadId = attemptId ?? r.AcquisitionDownloadId;
            r.Failure = null;
            r.Error = null;
        }, ct, _ => new ActivityNote(
            TrackActivityKind.DownloadCompleted, "Download completed; the file waits for identification and import."));

    public Task<bool> MarkCompletedAsync(int id, CancellationToken ct) =>
        TransitionAsync(id, DownloadRequestStatus.Completed, r => r.FinishedAt = time.GetUtcNow(), ct);

    public Task<bool> RetryLaterAsync(
        int id, AcquisitionFailure failure, string error, DateTimeOffset notBefore, int? attemptId, CancellationToken ct) =>
        TransitionAsync(id, DownloadRequestStatus.Queued, r =>
        {
            RecordFailure(r, failure, error, attemptId);
            r.NextAttemptAt = notBefore;
        }, ct, r => new ActivityNote(
            TrackActivityKind.DownloadRetryScheduled,
            $"Attempt {r.AttemptCount} failed ({failure}); another attempt is scheduled.", r.Error));

    public Task<bool> FailAsync(int id, AcquisitionFailure failure, string error, int? attemptId, CancellationToken ct) =>
        TransitionAsync(id, DownloadRequestStatus.Failed, r =>
        {
            RecordFailure(r, failure, error, attemptId);
            r.FinishedAt = time.GetUtcNow();
        }, ct, r => new ActivityNote(
            TrackActivityKind.DownloadFailed,
            $"Download failed after {r.AttemptCount} attempt(s) ({failure}); no more attempts.", r.Error));

    public async Task<int> RequeueInterruptedAsync(CancellationToken ct)
    {
        var interrupted = await db.DownloadRequests
            .Where(r => r.Status == DownloadRequestStatus.Downloading).ToListAsync(ct);
        var requeued = 0;
        foreach (var request in interrupted)
        {
            if (await TransitionAsync(request, DownloadRequestStatus.Queued, r =>
                {
                    r.Failure = null;
                    r.Error = "Interrupted by a restart; queued again.";
                }, ct, _ => new ActivityNote(TrackActivityKind.DownloadInterrupted, "Interrupted by a restart; queued again.")))
                requeued++;
        }

        return requeued;
    }

    private static void RecordFailure(DownloadRequest request, AcquisitionFailure failure, string error, int? attemptId)
    {
        request.Failure = failure;
        request.Error = AcquisitionHistory.Truncate(error, DownloadRequest.ErrorMaxLength);
        request.AcquisitionDownloadId = attemptId ?? request.AcquisitionDownloadId;
    }

    private async Task<bool> TransitionAsync(
        int id, DownloadRequestStatus to, Action<DownloadRequest> apply, CancellationToken ct,
        Func<DownloadRequest, ActivityNote>? note = null)
    {
        var request = await FindAsync(id, ct);
        return request is not null && await TransitionAsync(request, to, apply, ct, note);
    }

    /// <summary>
    /// Applies the change only if the transition is allowed and the status is still the one read. The
    /// <paramref name="note"/> is saved with it, so the track's timeline gets the event if and only if the transition applied.
    /// </summary>
    private async Task<bool> TransitionAsync(
        DownloadRequest request, DownloadRequestStatus to, Action<DownloadRequest> apply, CancellationToken ct,
        Func<DownloadRequest, ActivityNote>? note = null)
    {
        if (!DownloadRequestStatuses.CanTransition(request.Status, to))
            return false;

        apply(request);
        request.Status = to;
        request.UpdatedAt = time.GetUtcNow();
        if (note is not null && request.TrackId is { } trackId)
        {
            var n = note(request);
            activity.Stage(trackId, n.Kind, n.Summary, n.Detail, new TrackActivityLinks
            {
                DownloadRequestId = request.Id, DownloadId = request.AcquisitionDownloadId,
                JobExecutionId = request.JobExecutionId, AttemptNumber = request.AttemptCount,
            });
        }

        return await SaveAsync(request, ct);
    }

    private sealed record ActivityNote(TrackActivityKind Kind, string Summary, string? Detail = null);

    private async Task<bool> SaveAsync(DownloadRequest request, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another caller changed the status first; forget our stale copy and the event that reported it.
            db.Entry(request).State = EntityState.Detached;
            foreach (var staged in db.ChangeTracker.Entries<TrackActivity>().Where(e => e.State == EntityState.Added).ToList())
                staged.State = EntityState.Detached;
            return false;
        }
    }

    private Task<DownloadRequest?> FindAsync(int id, CancellationToken ct) =>
        db.DownloadRequests.Where(r => r.Id == id).FirstOrDefaultAsync(ct);

    private Task<DownloadRequest?> FindVisibleAsync(int id, CancellationToken ct) =>
        db.DownloadRequests.Where(r => r.Id == id && r.RemovedAt == null).FirstOrDefaultAsync(ct);

    private Task<DownloadRequest?> FindActiveAsync(
        int trackId, (string Provider, string Source, string RemotePath) identity, CancellationToken ct) =>
        db.DownloadRequests.AsNoTracking()
            .Where(r => DownloadRequestStatuses.Active.Contains(r.Status)
                && (r.TrackId == trackId
                    || (r.Provider == identity.Provider && r.Source == identity.Source
                        && r.RemotePath == identity.RemotePath)))
            .OrderBy(r => r.Id)
            .FirstOrDefaultAsync(ct);

    private async Task<EnqueueResult> AlreadyActiveAsync(int id, CancellationToken ct) =>
        new(EnqueueOutcome.AlreadyActive, await GetAsync(id, ct));

    private IQueryable<DownloadRequest> Viewable() =>
        db.DownloadRequests.AsNoTracking()
            .Where(r => r.RemovedAt == null)
            .Include(r => r.Track).ThenInclude(t => t!.Artists).ThenInclude(a => a.Artist);

    private static DownloadRequestView ToView(DownloadRequest r) => new(
        r.Id,
        r.TrackId,
        r.Track?.Title,
        r.Track?.Artists.OrderBy(a => a.Id).Select(a => a.Artist.Name).ToList() ?? [],
        r.Provider,
        RemotePath.FileName(r.RemotePath),
        r.SizeBytes,
        r.BitrateKbps,
        r.Format,
        r.Status,
        r.AttemptCount,
        r.BytesTransferred,
        r.SizeBytes is > 0 ? (int)Math.Min(100, r.BytesTransferred * 100 / r.SizeBytes.Value) : null,
        r.NextAttemptAt,
        r.Failure,
        r.Error,
        r.JobExecutionId,
        r.CreatedAt,
        r.UpdatedAt,
        r.StartedAt,
        r.FinishedAt);
}
