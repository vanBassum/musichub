using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Acquisition;
using MusicHub.Api.Catalog;
using MusicHub.Api.Controllers;
using MusicHub.Api.Data;

namespace MusicHub.Api.Tests.Acquisition;

/// <summary>
/// Runs on a SQLite file so the filtered unique indexes and the concurrency token are real and several contexts can
/// compete; a second context stands for a restart.
/// </summary>
public sealed class DownloadQueueServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"musichub-queue-{Guid.NewGuid():N}.db");
    private readonly int _trackId;
    private readonly int _otherTrackId;

    public DownloadQueueServiceTests()
    {
        using var db = CreateDb();
        db.Database.EnsureCreated();
        var track = new Track { Title = "Song", CreatedAt = DateTimeOffset.UtcNow };
        track.Artists.Add(new TrackArtist { Artist = new Artist { Name = "Band" } });
        var other = new Track { Title = "Other", CreatedAt = DateTimeOffset.UtcNow };
        db.Tracks.AddRange(track, other);
        db.SaveChanges();
        _trackId = track.Id;
        _otherTrackId = other.Id;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite($"Data Source={_path};Pooling=False").Options);

    private static DownloadQueueService Queue(MusicHubDbContext db, TimeProvider? time = null) =>
        new(db, time ?? TimeProvider.System);

    private static AcquisitionCandidate Candidate(string path = @"Music\a\song.mp3", string source = "peer1") =>
        new("slskd", source, path, 1000, "mp3", 320);

    private async Task<int> EnqueueAsync(int? trackId = null, AcquisitionCandidate? candidate = null)
    {
        await using var db = CreateDb();
        var result = await Queue(db).EnqueueAsync(
            new EnqueueDownloadCommand(trackId ?? _trackId, candidate ?? Candidate(), "job-1"), default);
        Assert.Equal(EnqueueOutcome.Created, result.Outcome);
        return result.Request!.Id;
    }

    [Fact]
    public async Task Enqueue_persists_identity_and_provenance_and_survives_a_restart()
    {
        var id = await EnqueueAsync();

        await using var restarted = CreateDb();
        var view = await Queue(restarted).GetAsync(id, default);
        Assert.NotNull(view);
        Assert.Equal(DownloadRequestStatus.Queued, view.Status);
        Assert.Equal(_trackId, view.TrackId);
        Assert.Equal("Song", view.TrackTitle);
        Assert.Equal(["Band"], view.Artists);
        Assert.Equal("slskd", view.Provider);
        Assert.Equal("song.mp3", view.FileName);
        Assert.Equal("job-1", view.JobExecutionId);
        Assert.Equal(0, view.AttemptCount);
        var stored = await restarted.DownloadRequests.SingleAsync();
        Assert.Equal("peer1", stored.Source);
        Assert.Equal(@"Music\a\song.mp3", stored.RemotePath);
    }

    [Fact]
    public async Task Enqueueing_the_same_track_or_candidate_again_returns_the_active_request()
    {
        var id = await EnqueueAsync();

        await using var db = CreateDb();
        var sameTrack = await Queue(db).EnqueueAsync(new EnqueueDownloadCommand(_trackId, Candidate("other.mp3")), default);
        var sameCandidate = await Queue(db).EnqueueAsync(new EnqueueDownloadCommand(_otherTrackId, Candidate()), default);

        Assert.Equal(EnqueueOutcome.AlreadyActive, sameTrack.Outcome);
        Assert.Equal(id, sameTrack.Request!.Id);
        Assert.Equal(EnqueueOutcome.AlreadyActive, sameCandidate.Outcome);
        Assert.Equal(id, sameCandidate.Request!.Id);
        Assert.Equal(1, await db.DownloadRequests.CountAsync());
    }

    [Fact]
    public async Task Concurrent_enqueues_create_exactly_one_request()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = CreateDb();
            return await Queue(db).EnqueueAsync(new EnqueueDownloadCommand(_trackId, Candidate()), default);
        }));

        Assert.Equal(1, results.Count(r => r.Outcome == EnqueueOutcome.Created));
        Assert.Equal(7, results.Count(r => r.Outcome == EnqueueOutcome.AlreadyActive));
        Assert.Single(results.Select(r => r.Request!.Id).Distinct());
        await using var check = CreateDb();
        Assert.Equal(1, await check.DownloadRequests.CountAsync());
    }

    [Fact]
    public async Task A_missing_track_is_reported_and_nothing_is_stored()
    {
        await using var db = CreateDb();
        var result = await Queue(db).EnqueueAsync(new EnqueueDownloadCommand(9999, Candidate()), default);

        Assert.Equal(EnqueueOutcome.TrackNotFound, result.Outcome);
        Assert.Null(result.Request);
        Assert.Empty(db.DownloadRequests);
    }

    [Fact]
    public async Task A_finished_request_no_longer_blocks_a_new_one()
    {
        var id = await EnqueueAsync();
        await using (var db = CreateDb())
        {
            var queue = Queue(db);
            Assert.NotNull(await queue.ClaimNextAsync(default));
            Assert.True(await queue.FailAsync(id, AcquisitionFailure.TransferFailed, "The peer went away.", null, default));
        }

        var again = await EnqueueAsync();
        Assert.NotEqual(id, again);
    }

    [Fact]
    public async Task Status_transitions_follow_the_allowed_path_only()
    {
        var id = await EnqueueAsync();
        await using var db = CreateDb();
        var queue = Queue(db);

        Assert.False(await queue.MarkDownloadedAsync(id, null, default));
        Assert.False(await queue.MarkCompletedAsync(id, default));
        Assert.False(await queue.ReportProgressAsync(id, 10, default));
        var claim = await queue.ClaimNextAsync(default);
        Assert.Equal(id, claim!.RequestId);
        Assert.Equal(1, claim.AttemptNumber);
        Assert.Equal("peer1", claim.Candidate.Source);
        Assert.True(await queue.ReportProgressAsync(id, 250, default));
        Assert.False(await queue.MarkCompletedAsync(id, default));
        Assert.True(await queue.MarkDownloadedAsync(id, null, default));
        Assert.False(await queue.ReportProgressAsync(id, 500, default));
        Assert.True(await queue.MarkCompletedAsync(id, default));

        var view = await queue.GetAsync(id, default);
        Assert.Equal(DownloadRequestStatus.Completed, view!.Status);
        Assert.NotNull(view.FinishedAt);
        Assert.False(await queue.FailAsync(id, AcquisitionFailure.ProcessingFailed, "Too late.", null, default));
        Assert.Null(await queue.ClaimNextAsync(default));
    }

    [Fact]
    public void The_transition_table_allows_no_move_out_of_a_final_status_and_no_skipping()
    {
        var all = Enum.GetValues<DownloadRequestStatus>();
        foreach (var to in all)
        {
            Assert.False(DownloadRequestStatuses.CanTransition(DownloadRequestStatus.Completed, to));
            Assert.False(DownloadRequestStatuses.CanTransition(DownloadRequestStatus.Failed, to));
            Assert.False(DownloadRequestStatuses.CanTransition(to, to));
        }

        Assert.False(DownloadRequestStatuses.CanTransition(DownloadRequestStatus.Queued, DownloadRequestStatus.Completed));
        Assert.False(DownloadRequestStatuses.CanTransition(DownloadRequestStatus.Queued, DownloadRequestStatus.Downloaded));
    }

    [Fact]
    public async Task Progress_is_reported_as_a_percentage_of_the_announced_size()
    {
        var id = await EnqueueAsync();
        await using var db = CreateDb();
        var queue = Queue(db);
        await queue.ClaimNextAsync(default);
        await queue.ReportProgressAsync(id, 250, default);

        Assert.Equal(25, (await queue.GetAsync(id, default))!.ProgressPercent);
    }

    [Fact]
    public async Task Two_workers_cannot_claim_the_same_request()
    {
        await EnqueueAsync();
        await using var first = CreateDb();
        await using var second = CreateDb();
        var seenByFirst = await first.DownloadRequests.ToListAsync();
        Assert.Single(seenByFirst);

        Assert.NotNull(await Queue(second).ClaimNextAsync(default));
        // The first context still holds the old Queued copy; its claim must lose instead of overwriting.
        Assert.Null(await Queue(first).ClaimNextAsync(default));
    }

    [Fact]
    public async Task A_retry_waits_for_its_time_and_keeps_the_failure_visible()
    {
        var id = await EnqueueAsync();
        var clock = new FixedTime(DateTimeOffset.Parse("2026-10-21T12:00:00Z"));
        await using var db = CreateDb();
        var queue = Queue(db, clock);
        await queue.ClaimNextAsync(default);
        Assert.True(await queue.RetryLaterAsync(
            id, AcquisitionFailure.TimedOut, "Timed out.", clock.GetUtcNow().AddMinutes(5), null, default));

        Assert.Null(await queue.ClaimNextAsync(default));
        var view = await queue.GetAsync(id, default);
        Assert.Equal(DownloadRequestStatus.Queued, view!.Status);
        Assert.Equal(AcquisitionFailure.TimedOut, view.Failure);
        Assert.Equal("Timed out.", view.Error);

        clock.Advance(TimeSpan.FromMinutes(6));
        var claim = await queue.ClaimNextAsync(default);
        Assert.Equal(2, claim!.AttemptNumber);
    }

    [Fact]
    public async Task Restart_recovery_requeues_interrupted_transfers_and_leaves_downloaded_ones()
    {
        var interrupted = await EnqueueAsync();
        var downloaded = await EnqueueAsync(_otherTrackId, Candidate("two.mp3"));
        await using (var before = CreateDb())
        {
            var queue = Queue(before);
            await queue.ClaimNextAsync(default);
            await queue.ClaimNextAsync(default);
            await queue.MarkDownloadedAsync(downloaded, null, default);
        }

        await using var restarted = CreateDb();
        Assert.Equal(1, await Queue(restarted).RequeueInterruptedAsync(default));

        var view = await Queue(restarted).GetAsync(interrupted, default);
        Assert.Equal(DownloadRequestStatus.Queued, view!.Status);
        Assert.Equal(1, view.AttemptCount);
        Assert.Equal(DownloadRequestStatus.Downloaded, (await Queue(restarted).GetAsync(downloaded, default))!.Status);
    }

    [Fact]
    public async Task Deleting_the_track_keeps_the_request()
    {
        var id = await EnqueueAsync();
        await using var db = CreateDb();
        db.Tracks.Remove(await db.Tracks.SingleAsync(t => t.Id == _trackId));
        await db.SaveChangesAsync();

        var view = await Queue(db).GetAsync(id, default);
        Assert.NotNull(view);
        Assert.Null(view.TrackId);
        Assert.Empty(view.Artists);
    }

    [Fact]
    public async Task The_api_lists_newest_first_filters_by_status_and_hides_the_source()
    {
        var first = await EnqueueAsync();
        var second = await EnqueueAsync(_otherTrackId, Candidate("two.mp3", "peer2"));
        await using var db = CreateDb();
        await Queue(db).ClaimNextAsync(default);
        var controller = new DownloadsController(Queue(db));

        var all = Assert.IsType<DownloadRequestPage>(Assert.IsType<OkObjectResult>(
            await controller.List(null, 1, 25)).Value);
        var queued = Assert.IsType<DownloadRequestPage>(Assert.IsType<OkObjectResult>(
            await controller.List(DownloadRequestStatus.Queued, 1, 25)).Value);
        var one = Assert.IsType<DownloadRequestView>(Assert.IsType<OkObjectResult>(
            await controller.Get(first, default)).Value);

        Assert.Equal([second, first], all.Items.Select(i => i.Id));
        Assert.Equal([second], queued.Items.Select(i => i.Id));
        Assert.Equal(DownloadRequestStatus.Downloading, one.Status);
        Assert.IsType<NotFoundResult>(await controller.Get(9999, default));
        Assert.DoesNotContain(nameof(DownloadRequest.Source),
            typeof(DownloadRequestView).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain(nameof(DownloadRequest.RemotePath),
            typeof(DownloadRequestView).GetProperties().Select(p => p.Name));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
