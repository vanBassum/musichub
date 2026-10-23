using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Acquisition;
using MusicHub.Api.Catalog;
using MusicHub.Api.Controllers;
using MusicHub.Api.Data;

namespace MusicHub.Api.Tests.Acquisition;

/// <summary>
/// The per-track acquisition timeline: what the search, the queue and the download history write, in order, with their
/// counts and correlation, surviving a new context (a restart), bounded, and free of paths, addresses and secrets.
/// </summary>
public sealed class TrackActivityTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly OffersProvider _provider = new();
    private readonly int _trackId;

    public TrackActivityTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
        _trackId = AddTrack("Wonderwall", "Oasis");
    }

    public void Dispose() => _connection.Dispose();

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(_connection).Options);

    private sealed class OffersProvider : IAcquisitionProvider
    {
        public string Name => "slskd";
        public List<AcquisitionCandidate> Offers { get; } = [];
        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<AcquisitionCandidate>> SearchAsync(AcquisitionQuery query, CancellationToken ct) =>
            Failure is { } failure ? throw failure : Task.FromResult<IReadOnlyList<AcquisitionCandidate>>(Offers.ToList());

        public Task<AcquisitionDownloadOutcome> DownloadAsync(AcquisitionCandidate candidate, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AcquisitionDownloadOutcome> ResumeDownloadAsync(
            AcquisitionCandidate candidate, Func<long, Task> reportProgress, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private int AddTrack(string title, string artist)
    {
        using var db = CreateDb();
        var track = new Track { Title = title, CreatedAt = DateTimeOffset.UtcNow };
        track.Artists.Add(new TrackArtist { Artist = new Artist { Name = artist, CreatedAt = DateTimeOffset.UtcNow } });
        db.Tracks.Add(track);
        db.SaveChanges();
        return track.Id;
    }

    private static AcquisitionCandidate Offer(string path = @"Oasis\Wonderwall.mp3") =>
        new("slskd", "peer1", path, 3000, "mp3", 320, HasFreeSlot: true, QueueLength: 0);

    private async Task<TrackDiscovery> SearchAsync(string? job = null)
    {
        await using var db = CreateDb();
        var activity = new TrackActivityLog(db, TimeProvider.System);
        var acquisition = new TrackAcquisition(db, new AcquisitionHistory(db, TimeProvider.System, activity), activity, _provider,
            Microsoft.Extensions.Options.Options.Create(new DownloadWorkerOptions()), TimeProvider.System);
        return await acquisition.DiscoverAsync(_trackId, default, job);
    }

    private async Task<IReadOnlyList<TrackActivityView>> TimelineAsync(int? trackId = null)
    {
        await using var db = CreateDb();
        return (await new TrackActivityLog(db, TimeProvider.System).ForTrackAsync(trackId ?? _trackId, 1, 200, default))!.Items;
    }

    private DownloadQueueService Queue(MusicHubDbContext db) =>
        new(db, TimeProvider.System, new TrackActivityLog(db, TimeProvider.System));

    [Fact]
    public async Task A_track_searched_twice_shows_both_attempts_with_their_counts_and_outcomes()
    {
        _provider.Offers.Add(Offer());
        _provider.Offers.Add(new AcquisitionCandidate("slskd", "peer2", @"Other\Unrelated.mp3", 10, "mp3", 128));
        var first = await SearchAsync("job-1");
        _provider.Offers.Clear();
        var second = await SearchAsync("job-2");

        var timeline = await TimelineAsync();
        Assert.Equal(
            [TrackActivityKind.SearchStarted, TrackActivityKind.SearchCompleted,
             TrackActivityKind.SearchStarted, TrackActivityKind.NotFound],
            timeline.Select(a => a.Kind));
        Assert.Equal((2, 1, 1), (timeline[1].ResultCount, timeline[1].PlausibleCount, timeline[1].EligibleCount));
        Assert.Equal((0, 0, 0), (timeline[3].ResultCount, timeline[3].PlausibleCount, timeline[3].EligibleCount));
        Assert.Equal(first.SearchId, timeline[0].SearchId);
        Assert.Equal(first.SearchId, timeline[1].SearchId);
        Assert.NotEqual(timeline[0].SearchId, timeline[2].SearchId);
        Assert.Equal(["job-1", "job-1", "job-2", "job-2"], timeline.Select(a => a.JobExecutionId));
        Assert.Null(second.SearchId);
    }

    [Fact]
    public async Task A_search_without_results_shows_the_search_and_the_not_found_outcome()
    {
        await SearchAsync();

        var timeline = await TimelineAsync();
        Assert.Equal([TrackActivityKind.SearchStarted, TrackActivityKind.NotFound], timeline.Select(a => a.Kind));
        Assert.Equal(0, timeline[1].ResultCount);
        Assert.Contains("Nothing to download", timeline[1].Summary);
    }

    [Fact]
    public async Task A_provider_that_cannot_search_is_shown_as_a_failed_search_not_as_not_found()
    {
        _provider.Failure = new AcquisitionUnavailableException("slskd at http://10.0.0.5:5030 refused (api_key=abc123)");
        await SearchAsync();

        var timeline = await TimelineAsync();
        Assert.Equal([TrackActivityKind.SearchStarted, TrackActivityKind.SearchFailed], timeline.Select(a => a.Kind));
        Assert.DoesNotContain("10.0.0.5", timeline[1].Detail);
        Assert.DoesNotContain("abc123", timeline[1].Detail);
    }

    [Fact]
    public async Task A_candidate_rejected_before_is_not_eligible_and_the_count_says_so()
    {
        _provider.Offers.Add(Offer());
        await using (var db = CreateDb())
        {
            var history = new AcquisitionHistory(db, TimeProvider.System, new TrackActivityLog(db, TimeProvider.System));
            await history.RejectedAsync(await history.BeginAsync(Offer(), _trackId, default), "Another recording.", default);
        }

        await SearchAsync();

        var completed = (await TimelineAsync()).Last();
        Assert.Equal(TrackActivityKind.NotFound, completed.Kind);
        Assert.Equal((1, 1, 0), (completed.ResultCount, completed.PlausibleCount, completed.EligibleCount));
    }

    [Fact]
    public async Task The_download_and_the_import_are_distinct_events_correlated_to_the_search_and_the_request()
    {
        _provider.Offers.Add(Offer());
        var discovery = await SearchAsync("job-1");
        int requestId;
        await using (var db = CreateDb())
        {
            var created = await Queue(db).EnqueueAsync(
                new EnqueueDownloadCommand(_trackId, discovery.Fresh[0], "job-1", discovery.SearchId), default);
            requestId = created.Request!.Id;
        }

        int downloadId;
        await using (var db = CreateDb())
        {
            var queue = Queue(db);
            var claim = (await queue.ClaimNextAsync(default))!;
            var history = new AcquisitionHistory(db, TimeProvider.System, new TrackActivityLog(db, TimeProvider.System));
            var download = await history.BeginAsync(claim.Candidate, _trackId, default, "job-1");
            downloadId = download.Id;
            await queue.MarkDownloadedAsync(requestId, download.Id, default);
            await history.AcceptedAsync(download, await NewFileIdAsync(db), "Identified.", default);
        }

        var timeline = await TimelineAsync();
        Assert.Equal(
            [TrackActivityKind.SearchStarted, TrackActivityKind.SearchCompleted, TrackActivityKind.DownloadQueued,
             TrackActivityKind.DownloadStarted, TrackActivityKind.DownloadCompleted, TrackActivityKind.ImportAccepted],
            timeline.Select(a => a.Kind));
        Assert.All(timeline.Skip(2).Take(3), a => Assert.Equal(requestId, a.DownloadRequestId));
        Assert.Equal(discovery.SearchId, timeline[2].SearchId);
        Assert.Equal(1, timeline[3].AttemptNumber);
        Assert.Equal(downloadId, timeline[5].AcquisitionDownloadId);
        Assert.Equal(timeline.OrderBy(a => a.OccurredAt).Select(a => a.Id), timeline.Select(a => a.Id));
    }

    private static async Task<int> NewFileIdAsync(MusicHubDbContext db)
    {
        var file = new MusicHub.Api.Ingestion.FileRecord
        {
            OriginalFilename = "a.mp3", StorageLocation = "test", StorageKey = Guid.NewGuid().ToString("N"),
            Source = "acquisition:slskd", StartedAt = DateTimeOffset.UtcNow,
        };
        db.Files.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    [Fact]
    public async Task A_rejected_import_and_each_failed_attempt_are_visible_with_their_attempt_numbers()
    {
        int requestId;
        await using (var db = CreateDb())
            requestId = (await Queue(db).EnqueueAsync(new EnqueueDownloadCommand(_trackId, Offer()), default)).Request!.Id;

        await using (var db = CreateDb())
        {
            var queue = Queue(db);
            await queue.ClaimNextAsync(default);
            Assert.True(await queue.RetryLaterAsync(
                requestId, AcquisitionFailure.TransferFailed, "Transfer failed.", DateTimeOffset.UtcNow.AddSeconds(-1), null, default));
        }

        await using (var db = CreateDb())
        {
            var queue = Queue(db);
            await queue.ClaimNextAsync(default);
            Assert.True(await queue.FailAsync(requestId, AcquisitionFailure.Rejected, "The source refused.", null, default));
        }

        await using (var db = CreateDb())
        {
            var history = new AcquisitionHistory(db, TimeProvider.System, new TrackActivityLog(db, TimeProvider.System));
            var download = await history.BeginAsync(Offer(), _trackId, default);
            await history.RejectedAsync(download, "Not the wanted recording.", default);
        }

        var timeline = await TimelineAsync();
        Assert.Equal(
            [TrackActivityKind.DownloadQueued, TrackActivityKind.DownloadStarted, TrackActivityKind.DownloadRetryScheduled,
             TrackActivityKind.DownloadStarted, TrackActivityKind.DownloadFailed, TrackActivityKind.ImportRejected],
            timeline.Select(a => a.Kind));
        Assert.Equal([null, 1, 1, 2, 2, null], timeline.Select(a => a.AttemptNumber));
        Assert.Equal("The source refused.", timeline[4].Detail);
        Assert.Equal("Not the wanted recording.", timeline[5].Detail);
    }

    [Fact]
    public async Task A_transition_that_lost_the_race_leaves_no_event()
    {
        int requestId;
        await using (var db = CreateDb())
            requestId = (await Queue(db).EnqueueAsync(new EnqueueDownloadCommand(_trackId, Offer()), default)).Request!.Id;

        await using var first = CreateDb();
        await using var second = CreateDb();
        var firstQueue = Queue(first);
        var secondQueue = Queue(second);
        // Both read Queued; only one claim applies.
        var claims = await Task.WhenAll(firstQueue.ClaimNextAsync(default), secondQueue.ClaimNextAsync(default));

        Assert.Single(claims, c => c is not null);
        Assert.Equal(1, (await TimelineAsync()).Count(a => a.Kind == TrackActivityKind.DownloadStarted));
        Assert.Equal(requestId, claims.Single(c => c is not null)!.RequestId);
    }

    [Fact]
    public async Task Interrupted_and_manual_retries_are_events_and_the_history_survives_a_restart()
    {
        int requestId;
        await using (var db = CreateDb())
            requestId = (await Queue(db).EnqueueAsync(new EnqueueDownloadCommand(_trackId, Offer()), default)).Request!.Id;
        await using (var db = CreateDb())
        {
            await Queue(db).ClaimNextAsync(default);
            Assert.Equal(1, await Queue(CreateDb()).RequeueInterruptedAsync(default));
        }

        await using (var db = CreateDb())
        {
            var queue = Queue(db);
            await queue.ClaimNextAsync(default);
            await queue.FailAsync(requestId, AcquisitionFailure.TransferFailed, "Gone.", null, default);
            Assert.Equal(RetryOutcome.Retried, (await queue.RetryAsync(requestId, default)).Outcome);
        }

        var timeline = await TimelineAsync();
        Assert.Equal(
            [TrackActivityKind.DownloadQueued, TrackActivityKind.DownloadStarted, TrackActivityKind.DownloadInterrupted,
             TrackActivityKind.DownloadStarted, TrackActivityKind.DownloadFailed, TrackActivityKind.DownloadRetriedManually],
            timeline.Select(a => a.Kind));
    }

    [Fact]
    public async Task Tracks_never_searched_or_downloaded_have_an_empty_timeline_and_unknown_tracks_none()
    {
        var other = AddTrack("Song", "Band");
        await using var db = CreateDb();
        var log = new TrackActivityLog(db, TimeProvider.System);

        var empty = await log.ForTrackAsync(other, 1, 50, default);
        Assert.NotNull(empty);
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.Total);
        Assert.Null(await log.ForTrackAsync(9999, 1, 50, default));
    }

    [Fact]
    public async Task The_timeline_is_paged_in_order_and_the_page_is_bounded()
    {
        await using var db = CreateDb();
        var log = new TrackActivityLog(db, TimeProvider.System);
        for (var i = 1; i <= 5; i++)
            await log.RecordAsync(_trackId, TrackActivityKind.SearchStarted, $"Event {i}", default);

        var page2 = (await log.ForTrackAsync(_trackId, 2, 2, default))!;
        Assert.Equal(["Event 3", "Event 4"], page2.Items.Select(a => a.Summary));
        Assert.Equal((5, 2, 2), (page2.Total, page2.Page, page2.PageSize));

        var clamped = (await log.ForTrackAsync(_trackId, -3, 100_000, default))!;
        Assert.Equal((1, TrackActivityLog.MaxPageSize), (clamped.Page, clamped.PageSize));
        Assert.Equal(5, clamped.Items.Count);
        Assert.Equal(1, (await log.ForTrackAsync(_trackId, 1, 0, default))!.PageSize);
    }

    [Fact]
    public async Task The_latest_events_of_many_tracks_come_in_one_bounded_query()
    {
        var other = AddTrack("Song", "Band");
        var untouched = AddTrack("Quiet", "Band");
        await using var db = CreateDb();
        var log = new TrackActivityLog(db, TimeProvider.System);
        for (var i = 1; i <= 4; i++)
            await log.RecordAsync(_trackId, TrackActivityKind.SearchStarted, $"A{i}", default);
        await log.RecordAsync(other, TrackActivityKind.NotFound, "B1", default);

        var latest = await log.LatestForTracksAsync([_trackId, other, untouched, _trackId], 2, default);

        Assert.Equal(["A3", "A4"], latest[_trackId].Select(a => a.Summary));
        Assert.Equal(["B1"], latest[other].Select(a => a.Summary));
        Assert.False(latest.ContainsKey(untouched));
        Assert.Empty(await log.LatestForTracksAsync([], 2, default));
        Assert.Equal(4, (await log.LatestForTracksAsync([_trackId], 1000, default))[_trackId].Count);
    }

    [Fact]
    public async Task Deleting_a_track_deletes_its_timeline_and_only_its_own()
    {
        var other = AddTrack("Song", "Band");
        await using (var db = CreateDb())
        {
            var log = new TrackActivityLog(db, TimeProvider.System);
            await log.RecordAsync(_trackId, TrackActivityKind.SearchStarted, "Mine", default);
            await log.RecordAsync(other, TrackActivityKind.SearchStarted, "Theirs", default);
        }

        await using (var db = CreateDb())
        {
            db.Tracks.Remove(await db.Tracks.SingleAsync(t => t.Id == _trackId));
            await db.SaveChangesAsync();
        }

        await using var check = CreateDb();
        Assert.Equal(["Theirs"], await check.TrackActivities.Select(a => a.Summary).ToListAsync());
    }

    [Theory]
    [InlineData(@"Failed reading C:\Music\Secret\song.mp3 now", @"C:\Music\Secret")]
    [InlineData(@"Failed reading \\nas\share\song.mp3 now", "nas")]
    [InlineData("Failed reading /srv/music/inbox/song.mp3 now", "/srv")]
    [InlineData(@"Remote Users\peer\Oasis\song.mp3 refused", "peer")]
    [InlineData("GET http://user:pw@10.0.0.5:5030/api/v0/search failed", "10.0.0.5")]
    [InlineData("Call failed: api_key=abc123 rejected", "abc123")]
    [InlineData("Call failed: Authorization: Bearer abc.def.ghi", "abc.def")]
    [InlineData("password: hunter2", "hunter2")]
    public void Sanitizing_removes_paths_addresses_and_secrets(string text, string leaked)
    {
        var safe = TrackActivityLog.Sanitize(text, 500);

        Assert.DoesNotContain(leaked, safe);
        Assert.NotEmpty(safe);
    }

    [Fact]
    public void Sanitizing_keeps_plain_sentences_and_cuts_to_the_limit()
    {
        Assert.Equal("Search finished: 12 results, 3 plausible MP3s, 2 eligible.",
            TrackActivityLog.Sanitize("Search finished: 12 results, 3 plausible MP3s, 2 eligible.", 500));
        Assert.Equal(10, TrackActivityLog.Sanitize(new string('a', 100), 10).Length);
    }

    [Fact]
    public async Task What_is_stored_is_sanitized_whatever_the_caller_passed()
    {
        await using var db = CreateDb();
        await new TrackActivityLog(db, TimeProvider.System).RecordAsync(
            _trackId, TrackActivityKind.DownloadFailed, @"Could not read C:\data\x.mp3", default,
            detail: "token=s3cr3t at https://slskd.local/api");

        var stored = (await TimelineAsync()).Single();
        Assert.DoesNotContain(@"C:\data", stored.Summary);
        Assert.DoesNotContain("s3cr3t", stored.Detail);
        Assert.DoesNotContain("slskd.local", stored.Detail);
    }

    [Fact]
    public async Task The_activity_endpoints_return_the_page_404_for_an_unknown_track_and_reject_bad_ids()
    {
        await using var db = CreateDb();
        var log = new TrackActivityLog(db, TimeProvider.System);
        await log.RecordAsync(_trackId, TrackActivityKind.SearchStarted, "Started", default);
        var controller = new TrackActivityController(log);

        var page = Assert.IsType<OkObjectResult>(await controller.ForTrack(_trackId));
        Assert.Single(Assert.IsType<TrackActivityPage>(page.Value).Items);
        Assert.IsType<NotFoundResult>(await controller.ForTrack(9999));

        var latest = Assert.IsType<OkObjectResult>(await controller.Latest($"{_trackId}, 77"));
        Assert.Single(Assert.IsType<Dictionary<int, IReadOnlyList<TrackActivityView>>>(latest.Value));
        Assert.IsType<BadRequestObjectResult>(await controller.Latest("1,x"));
        Assert.IsType<BadRequestObjectResult>(await controller.Latest(
            string.Join(',', Enumerable.Range(1, TrackActivityLog.MaxTracksPerRequest + 1))));
    }
}
