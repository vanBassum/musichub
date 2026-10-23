using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MusicHub.Api.Acquisition;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;

namespace MusicHub.Api.Tests.Acquisition;

/// <summary>
/// An offline source ends its request without a same-peer retry, and the track moves on to the next eligible candidate
/// or to a scheduled fresh search with a bounded backoff. A real queue on SQLite; the provider is scripted.
/// </summary>
public sealed class AcquisitionFailoverTests : IDisposable
{
    // A file, not memory: the workers of the concurrency test compete on separate connections, as in production.
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"musichub-failover-{Guid.NewGuid():N}.db");
    private readonly ScriptedProvider _provider = new();
    private readonly FixedTime _time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
    private readonly DownloadWorkerOptions _settings = new() { ResearchBaseDelayMinutes = 10, ResearchMaxDelayMinutes = 60, ResearchMaxAttempts = 2 };

    public AcquisitionFailoverTests()
    {
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ScriptedProvider : IAcquisitionProvider
    {
        public string Name => "slskd";
        public List<AcquisitionCandidate> Offers { get; } = [];
        public HashSet<string> OfflineSources { get; } = [];
        public int Searches { get; private set; }

        public Task<IReadOnlyList<AcquisitionCandidate>> SearchAsync(AcquisitionQuery query, CancellationToken ct)
        {
            Searches++;
            return Task.FromResult<IReadOnlyList<AcquisitionCandidate>>(Offers.ToList());
        }

        public Task<AcquisitionDownloadOutcome> DownloadAsync(AcquisitionCandidate candidate, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AcquisitionDownloadOutcome> LocateDownloadAsync(AcquisitionCandidate candidate, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AcquisitionDownloadOutcome> ResumeDownloadAsync(
            AcquisitionCandidate candidate, Func<long, Task> reportProgress, CancellationToken ct) =>
            Task.FromResult(OfflineSources.Contains(candidate.Source)
                ? AcquisitionDownloadOutcome.Failed("slskd", AcquisitionFailure.SourceOffline, $"{candidate.Source} is offline.")
                : AcquisitionDownloadOutcome.Failed("slskd", AcquisitionFailure.TransferFailed, "Not scripted."));
    }

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite($"Data Source={_path};Pooling=False").Options);

    private static AcquisitionCandidate Offer(string source, int bitrate) =>
        new("slskd", source, $@"Music\Oasis\Wonderwall {source}.mp3", 4_000_000, "mp3", bitrate);

    private async Task<int> AddTrackAsync()
    {
        await using var db = CreateDb();
        var track = new Track { Title = "Wonderwall", CreatedAt = DateTimeOffset.UtcNow };
        track.Artists.Add(new TrackArtist { Artist = new Artist { Name = "Oasis", CreatedAt = DateTimeOffset.UtcNow } });
        db.Tracks.Add(track);
        await db.SaveChangesAsync();
        return track.Id;
    }

    /// <summary>One scope of the services the worker uses, on its own context.</summary>
    private sealed record Scope(
        MusicHubDbContext Db, IDownloadQueueService Queue, AcquisitionFailover Failover, DownloadProcessor Processor);

    private Scope CreateScope()
    {
        var db = CreateDb();
        var options = Options.Create(_settings);
        var activity = new TrackActivityLog(db, _time);
        var history = new AcquisitionHistory(db, _time, activity);
        var queue = new DownloadQueueService(db, _time, activity);
        var failover = new AcquisitionFailover(
            db, new TrackAcquisition(db, history, activity, _provider, options, _time), queue, options, _time,
            NullLogger<AcquisitionFailover>.Instance);
        return new Scope(db, queue, failover,
            new DownloadProcessor(queue, _provider, history, failover, options, _time, NullLogger<DownloadProcessor>.Instance));
    }

    private async Task ProcessNextAsync()
    {
        var scope = CreateScope();
        var claim = await scope.Queue.ClaimNextAsync(default);
        Assert.NotNull(claim);
        await scope.Processor.ProcessAsync(claim, default);
    }

    private async Task<int> EnqueueAsync(int trackId, AcquisitionCandidate candidate)
    {
        var result = await CreateScope().Queue.EnqueueAsync(new EnqueueDownloadCommand(trackId, candidate), default);
        return result.Request!.Id;
    }

    [Fact]
    public async Task An_offline_source_ends_its_request_and_the_next_eligible_candidate_is_queued()
    {
        var trackId = await AddTrackAsync();
        _provider.Offers.AddRange([Offer("peer1", 320), Offer("peer2", 256)]);
        _provider.OfflineSources.Add("peer1");
        var first = await EnqueueAsync(trackId, Offer("peer1", 320));

        await ProcessNextAsync();

        var db = CreateDb();
        var failed = await db.DownloadRequests.SingleAsync(r => r.Id == first);
        Assert.Equal(DownloadRequestStatus.Failed, failed.Status);
        Assert.Equal(AcquisitionFailure.SourceOffline, failed.Failure);
        Assert.Equal(1, failed.AttemptCount);
        var next = await db.DownloadRequests.SingleAsync(r => r.Id != first);
        Assert.Equal("peer2", next.Source);
        Assert.Equal(DownloadRequestStatus.Queued, next.Status);
        Assert.Empty(db.PendingResearches);
        var attempt = await db.AcquisitionDownloads.SingleAsync();
        Assert.Equal(AcquisitionFailure.SourceOffline, attempt.Failure);
    }

    [Fact]
    public async Task A_source_that_was_offline_is_left_out_of_later_searches_until_the_cooldown_ends()
    {
        var trackId = await AddTrackAsync();
        _provider.Offers.Add(Offer("peer1", 320));
        _provider.OfflineSources.Add("peer1");
        await EnqueueAsync(trackId, Offer("peer1", 320));
        await ProcessNextAsync();

        var inside = await CreateScope().Failover.FailOverAsync(trackId, default);
        _time.Now = _time.Now.AddMinutes(_settings.SourceOfflineCooldownMinutes + 1);
        var after = await CreateScope().Failover.FailOverAsync(trackId, default);

        Assert.Equal(FailoverOutcome.ResearchScheduled, inside);
        Assert.Equal(FailoverOutcome.Enqueued, after);
    }

    [Fact]
    public async Task Without_an_eligible_candidate_a_fresh_search_is_scheduled_and_the_track_stays_wanted()
    {
        var trackId = await AddTrackAsync();
        _provider.Offers.Add(Offer("peer1", 320));
        _provider.OfflineSources.Add("peer1");
        var request = await EnqueueAsync(trackId, Offer("peer1", 320));

        await ProcessNextAsync();

        var db = CreateDb();
        var research = await db.PendingResearches.SingleAsync();
        Assert.Equal(trackId, research.TrackId);
        Assert.Equal(_time.Now.AddMinutes(10), research.NextSearchAt);
        Assert.Equal(DownloadRequestStatus.Failed, (await db.DownloadRequests.SingleAsync(r => r.Id == request)).Status);
        Assert.Empty(db.Files);
        // Nothing is claimable: the offline peer is not retried.
        Assert.Null(await CreateScope().Queue.ClaimNextAsync(default));
    }

    [Fact]
    public async Task A_scheduled_search_waits_until_it_is_due_then_backs_off_and_gives_up()
    {
        var trackId = await AddTrackAsync();
        await CreateScope().Failover.FailOverAsync(trackId, default);

        Assert.Equal(0, await CreateScope().Failover.RunDueAsync(default));

        _time.Now = _time.Now.AddMinutes(10);
        Assert.Equal(1, await CreateScope().Failover.RunDueAsync(default));
        var db = CreateDb();
        var research = await db.PendingResearches.SingleAsync();
        Assert.Equal(1, research.Searches);
        Assert.Equal(_time.Now.AddMinutes(20), research.NextSearchAt);

        _time.Now = _time.Now.AddMinutes(20);
        Assert.Equal(1, await CreateScope().Failover.RunDueAsync(default));
        Assert.Empty(CreateDb().PendingResearches);
    }

    [Fact]
    public async Task A_scheduled_search_that_finds_a_candidate_queues_it_and_ends_the_schedule()
    {
        var trackId = await AddTrackAsync();
        await CreateScope().Failover.FailOverAsync(trackId, default);
        _provider.Offers.Add(Offer("peer3", 320));
        _time.Now = _time.Now.AddMinutes(10);

        await CreateScope().Failover.RunDueAsync(default);

        var db = CreateDb();
        Assert.Empty(db.PendingResearches);
        Assert.Equal("peer3", (await db.DownloadRequests.SingleAsync()).Source);
    }

    [Fact]
    public async Task Two_workers_do_not_run_the_same_scheduled_search_twice()
    {
        var trackId = await AddTrackAsync();
        await CreateScope().Failover.FailOverAsync(trackId, default);
        _time.Now = _time.Now.AddMinutes(10);
        var searchesBefore = _provider.Searches;

        var ran = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(_ => Task.Run(() => CreateScope().Failover.RunDueAsync(default))));

        Assert.Equal(1, ran.Sum());
        Assert.Equal(searchesBefore + 1, _provider.Searches);
    }
}
