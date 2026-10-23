using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MusicHub.Api.Acquisition;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;
using MusicHub.Api.Identification;
using MusicHub.Api.Ingestion;
using MusicHub.Api.Storage;
using MusicHub.Api.Tests.Ingestion;

namespace MusicHub.Api.Tests.Acquisition;

/// <summary>
/// The worker and the processor against a real queue on a SQLite file (filtered unique indexes and the status
/// concurrency token are real, and separate contexts compete); the provider is scripted.
/// </summary>
public sealed class DownloadWorkerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"musichub-worker-{Guid.NewGuid():N}.db");
    private readonly ScriptedProvider _provider = new();
    private readonly List<int> _trackIds = [];

    public DownloadWorkerTests()
    {
        using var db = CreateDb();
        db.Database.EnsureCreated();
        for (var i = 0; i < 6; i++)
        {
            var track = new Track { Title = $"Song {i}", CreatedAt = DateTimeOffset.UtcNow };
            db.Tracks.Add(track);
            db.SaveChanges();
            _trackIds.Add(track.Id);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    private sealed class ScriptedProvider : IAcquisitionProvider
    {
        public string Name => "slskd";
        public Func<AcquisitionCandidate, Func<long, Task>, CancellationToken, Task<AcquisitionDownloadOutcome>> Script { get; set; } =
            (_, _, _) => throw new InvalidOperationException("No script.");
        private readonly List<string> _resumed = [];
        public IReadOnlyList<string> Resumed { get { lock (_resumed) return _resumed.ToList(); } }

        public Task<IReadOnlyList<AcquisitionCandidate>> SearchAsync(AcquisitionQuery query, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AcquisitionDownloadOutcome> DownloadAsync(AcquisitionCandidate candidate, CancellationToken ct) =>
            throw new NotSupportedException("The worker follows transfers; it never starts a plain download.");

        /// <summary>By default the downloaded file is not there, which ends the handoff of a request at once.</summary>
        public Func<AcquisitionCandidate, AcquisitionDownloadOutcome> Locate { get; set; } =
            _ => AcquisitionDownloadOutcome.Failed("slskd", AcquisitionFailure.FileNotAccessible, "Not in the downloads folder.");
        public int Located;

        public Task<AcquisitionDownloadOutcome> LocateDownloadAsync(AcquisitionCandidate candidate, CancellationToken ct)
        {
            Interlocked.Increment(ref Located);
            return Task.FromResult(Locate(candidate));
        }

        public Task<AcquisitionDownloadOutcome> ResumeDownloadAsync(
            AcquisitionCandidate candidate, Func<long, Task> reportProgress, CancellationToken ct)
        {
            lock (_resumed) _resumed.Add(candidate.Path);
            return Script(candidate, reportProgress, ct);
        }
    }

    private sealed class NeverCalledIdentifier : IRecordingIdentifier
    {
        public Task<IdentificationResult> IdentifyAsync(Stream audio, string? fileNameHint, CancellationToken ct) =>
            throw new InvalidOperationException("The handoff of these tests never gets as far as identifying.");
    }

    private sealed class Readable : IAcquiredFile
    {
        public string FileName => "song.mp3";
        public Stream OpenRead() => new MemoryStream([1, 2, 3]);
    }

    private static readonly AcquisitionDownloadOutcome Delivered = AcquisitionDownloadOutcome.Completed("slskd", new Readable());

    private static AcquisitionDownloadOutcome Failed(AcquisitionFailure failure) =>
        AcquisitionDownloadOutcome.Failed("slskd", failure, "slskd said no.");

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite($"Data Source={_path};Pooling=False").Options);

    private ServiceProvider Services(Action<DownloadWorkerOptions>? configure = null)
    {
        var settings = new DownloadWorkerOptions { PollIntervalSeconds = 1, RetryDelaySeconds = 30 };
        configure?.Invoke(settings);
        return new ServiceCollection()
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .AddSingleton(Options.Create(settings))
            .AddSingleton<IAcquisitionProvider>(_provider)
            .AddDbContext<MusicHubDbContext>(o => o.UseSqlite($"Data Source={_path};Pooling=False"))
            .AddScoped<IDownloadQueueService, DownloadQueueService>()
            .AddScoped<TrackActivityLog>()
            .AddScoped<AcquisitionHistory>()
            .AddScoped<TrackAcquisition>()
            .AddScoped<AcquisitionFailover>()
            .AddScoped<DownloadProcessor>()
            // The handoff after the download; these tests end it at once (the file is not located), see DownloadHandoffTests.
            .AddSingleton<IRecordingIdentifier>(new NeverCalledIdentifier())
            .AddSingleton<IFileStorage>(new FakeFileStorage())
            .AddSingleton<ActiveUploadTracker>()
            .AddScoped<FileIngestionService>()
            .AddScoped<AcquisitionHandoff>()
            .AddScoped<DownloadHandoffProcessor>()
            .BuildServiceProvider();
    }

    private static AcquisitionCandidate Candidate(int n) => new("slskd", "peer", $@"Music\a\song{n}.mp3", 1000, "mp3", 320);

    private static DownloadQueueService Queue(MusicHubDbContext db) =>
        new(db, TimeProvider.System, new TrackActivityLog(db, TimeProvider.System));

    private async Task<int> EnqueueAsync(int n)
    {
        await using var db = CreateDb();
        var result = await Queue(db)
            .EnqueueAsync(new EnqueueDownloadCommand(_trackIds[n], Candidate(n)), default);
        return result.Request!.Id;
    }

    private async Task<DownloadRequestView> ViewAsync(int id)
    {
        await using var db = CreateDb();
        return (await Queue(db).GetAsync(id, default))!;
    }

    private async Task ProcessOneAsync(ServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var claim = await scope.ServiceProvider.GetRequiredService<IDownloadQueueService>().ClaimNextAsync(ct);
        Assert.NotNull(claim);
        await scope.ServiceProvider.GetRequiredService<DownloadProcessor>().ProcessAsync(claim, ct);
    }

    [Fact]
    public async Task A_delivered_file_moves_the_request_to_downloaded_with_its_progress_and_attempt_record()
    {
        var id = await EnqueueAsync(0);
        _provider.Script = async (_, report, _) =>
        {
            await report(400);
            await report(1000);
            return Delivered;
        };
        using var services = Services();

        await ProcessOneAsync(services);

        var view = await ViewAsync(id);
        Assert.Equal(DownloadRequestStatus.Downloaded, view.Status);
        Assert.Equal(1000, view.BytesTransferred);
        Assert.Equal(100, view.ProgressPercent);
        await using var db = CreateDb();
        var request = await db.DownloadRequests.SingleAsync();
        Assert.NotNull(request.AcquisitionDownloadId);
        Assert.Equal(1, await db.AcquisitionDownloads.CountAsync());
    }

    [Fact]
    public async Task A_failed_attempt_is_queued_again_later_and_stays_diagnosable()
    {
        var id = await EnqueueAsync(0);
        _provider.Script = (_, _, _) => Task.FromResult(Failed(AcquisitionFailure.TransferFailed));
        using var services = Services();

        await ProcessOneAsync(services);

        var view = await ViewAsync(id);
        Assert.Equal(DownloadRequestStatus.Queued, view.Status);
        Assert.Equal(AcquisitionFailure.TransferFailed, view.Failure);
        Assert.Equal("slskd said no.", view.Error);
        Assert.True(view.NextAttemptAt > DateTimeOffset.UtcNow.AddSeconds(20));
        await using var db = CreateDb();
        var attempt = await db.AcquisitionDownloads.SingleAsync();
        Assert.Equal(AcquisitionDownloadState.Failed, attempt.State);
        Assert.Equal(AcquisitionFailure.TransferFailed, attempt.Failure);
        Assert.Null(await Queue(db).ClaimNextAsync(default));
    }

    [Fact]
    public async Task The_last_allowed_attempt_fails_the_request_for_good()
    {
        var id = await EnqueueAsync(0);
        _provider.Script = (_, _, _) => Task.FromResult(Failed(AcquisitionFailure.Unavailable));
        using var services = Services(o => { o.MaxAttempts = 1; });

        await ProcessOneAsync(services);

        var view = await ViewAsync(id);
        Assert.Equal(DownloadRequestStatus.Failed, view.Status);
        Assert.Equal(AcquisitionFailure.Unavailable, view.Failure);
        Assert.NotNull(view.FinishedAt);
    }

    [Theory]
    [InlineData(AcquisitionFailure.Rejected)]
    [InlineData(AcquisitionFailure.FileNotAccessible)]
    public async Task A_failure_another_attempt_cannot_change_is_final_at_once(AcquisitionFailure failure)
    {
        var id = await EnqueueAsync(0);
        _provider.Script = (_, _, _) => Task.FromResult(Failed(failure));
        using var services = Services();

        await ProcessOneAsync(services);

        Assert.Equal(DownloadRequestStatus.Failed, (await ViewAsync(id)).Status);
    }

    [Fact]
    public async Task An_unexpected_exception_is_recorded_without_its_message_and_retried()
    {
        var id = await EnqueueAsync(0);
        _provider.Script = (_, _, _) => throw new IOException("C:\\secret\\path");
        using var services = Services();

        await ProcessOneAsync(services);

        var view = await ViewAsync(id);
        Assert.Equal(DownloadRequestStatus.Queued, view.Status);
        Assert.Equal(AcquisitionFailure.ProcessingFailed, view.Failure);
        Assert.DoesNotContain("secret", view.Error);
        Assert.Contains("IOException", view.Error);
    }

    [Fact]
    public async Task Stopping_leaves_the_request_downloading_and_the_restart_requeues_and_resumes_it()
    {
        var id = await EnqueueAsync(0);
        using var stop = new CancellationTokenSource();
        _provider.Script = async (_, report, ct) =>
        {
            await report(300);
            await stop.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
            return Delivered;
        };
        using var services = Services();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProcessOneAsync(services, stop.Token));

        var stopped = await ViewAsync(id);
        Assert.Equal(DownloadRequestStatus.Downloading, stopped.Status);
        Assert.Equal(300, stopped.BytesTransferred);
        await using (var db = CreateDb())
            Assert.Equal(AcquisitionDownloadState.Cancelled, (await db.AcquisitionDownloads.SingleAsync()).State);

        await using (var db = CreateDb())
            Assert.Equal(1, await Queue(db).RequeueInterruptedAsync(default));
        _provider.Script = (_, _, _) => Task.FromResult(Delivered);
        await ProcessOneAsync(services);

        var resumed = await ViewAsync(id);
        Assert.Equal(DownloadRequestStatus.Downloaded, resumed.Status);
        Assert.Equal(2, resumed.AttemptCount);
        Assert.Equal(2, _provider.Resumed.Count);
    }

    [Fact]
    public async Task Concurrent_loops_process_every_request_exactly_once()
    {
        var ids = new List<int>();
        for (var n = 0; n < 6; n++)
            ids.Add(await EnqueueAsync(n));
        _provider.Script = async (_, _, ct) =>
        {
            await Task.Delay(20, ct);
            return Delivered;
        };
        using var services = Services(o => o.MaxConcurrentDownloads = 3);
        var migrations = new MigrationState();
        migrations.MarkComplete();
        var worker = new DownloadWorker(
            services.GetRequiredService<IServiceScopeFactory>(), migrations,
            services.GetRequiredService<IOptions<DownloadWorkerOptions>>(), NullLogger<DownloadWorker>.Instance);

        await worker.StartAsync(default);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        // Every request is downloaded once, then handed off once (which ends at once here: the file is not located).
        while (DateTimeOffset.UtcNow < deadline && (await StatusesAsync(ids)).Any(s => s != DownloadRequestStatus.Failed))
            await Task.Delay(50);
        await worker.StopAsync(default);

        Assert.All(await StatusesAsync(ids), s => Assert.Equal(DownloadRequestStatus.Failed, s));
        Assert.Equal(6, _provider.Located);
        Assert.Equal(6, _provider.Resumed.Count);
        Assert.Equal(6, _provider.Resumed.Distinct().Count());
        await using var db = CreateDb();
        Assert.All(await db.DownloadRequests.ToListAsync(), r => Assert.Equal(1, r.AttemptCount));
    }

    [Fact]
    public async Task The_worker_requeues_interrupted_requests_when_it_starts()
    {
        var id = await EnqueueAsync(0);
        await using (var db = CreateDb())
            Assert.NotNull(await Queue(db).ClaimNextAsync(default));
        _provider.Script = (_, _, _) => Task.FromResult(Delivered);
        using var services = Services();
        var migrations = new MigrationState();
        migrations.MarkComplete();
        var worker = new DownloadWorker(
            services.GetRequiredService<IServiceScopeFactory>(), migrations,
            services.GetRequiredService<IOptions<DownloadWorkerOptions>>(), NullLogger<DownloadWorker>.Instance);

        await worker.StartAsync(default);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline && (await ViewAsync(id)).Status != DownloadRequestStatus.Failed)
            await Task.Delay(50);
        await worker.StopAsync(default);

        var view = await ViewAsync(id);
        Assert.Equal(DownloadRequestStatus.Failed, view.Status);
        Assert.Equal(AcquisitionFailure.FileNotAccessible, view.Failure);
        Assert.Equal(2, view.AttemptCount);
    }

    private async Task<List<DownloadRequestStatus>> StatusesAsync(IEnumerable<int> ids)
    {
        var statuses = new List<DownloadRequestStatus>();
        foreach (var id in ids)
            statuses.Add((await ViewAsync(id)).Status);
        return statuses;
    }
}
