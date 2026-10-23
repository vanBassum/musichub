using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
/// The handoff of a downloaded request (Downloaded to Processing to Completed or Failed) against a real queue and
/// database on a SQLite file; the provider, the identification and the storage are scripted.
/// </summary>
public sealed class DownloadHandoffTests : IDisposable
{
    private static readonly byte[] Bytes = [1, 2, 3, 4, 5];
    private static readonly string BytesHash = Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"musichub-handoff-{Guid.NewGuid():N}.db");
    private readonly string _source = Path.Combine(Path.GetTempPath(), $"musichub-handoff-{Guid.NewGuid():N}.mp3");
    private readonly Provider _provider = new();
    private readonly Identifier _identifier = new();
    private readonly FakeFileStorage _storage = new();
    private readonly int _trackId;

    public DownloadHandoffTests()
    {
        File.WriteAllBytes(_source, Bytes);
        using var db = CreateDb();
        db.Database.EnsureCreated();
        var track = new Track { Title = "Song", CreatedAt = DateTimeOffset.UtcNow };
        track.Artists.Add(new TrackArtist { Artist = new Artist { Name = "Artist", CreatedAt = DateTimeOffset.UtcNow } });
        db.Tracks.Add(track);
        db.SaveChanges();
        _trackId = track.Id;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
        File.Delete(_source);
    }

    private sealed class Provider : IAcquisitionProvider
    {
        public string Name => "slskd";
        public bool SourceExists { get; set; } = true;
        public string SourcePath { get; set; } = "";
        public int Located;

        public Task<IReadOnlyList<AcquisitionCandidate>> SearchAsync(AcquisitionQuery query, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AcquisitionCandidate>>([]);

        public Task<AcquisitionDownloadOutcome> DownloadAsync(AcquisitionCandidate candidate, CancellationToken ct) =>
            throw new NotSupportedException("The handoff never downloads.");

        public Task<AcquisitionDownloadOutcome> ResumeDownloadAsync(
            AcquisitionCandidate candidate, Func<long, Task> reportProgress, CancellationToken ct) =>
            throw new NotSupportedException("The handoff never follows a transfer: that could download again.");

        public Task<AcquisitionDownloadOutcome> LocateDownloadAsync(AcquisitionCandidate candidate, CancellationToken ct)
        {
            Interlocked.Increment(ref Located);
            return Task.FromResult(SourceExists
                ? AcquisitionDownloadOutcome.Completed("slskd", new LocalAcquiredFile("song.mp3", SourcePath))
                : AcquisitionDownloadOutcome.Failed("slskd", AcquisitionFailure.FileNotAccessible, "The file is not in the downloads folder."));
        }
    }

    private sealed class Identifier : IRecordingIdentifier
    {
        public Func<IdentificationResult> Script { get; set; } = () => throw new InvalidOperationException("No script.");
        public int Calls;

        public Task<IdentificationResult> IdentifyAsync(Stream audio, string? fileNameHint, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(Script());
        }
    }

    private static IdentificationResult Identified(string title, params string[] artists) =>
        IdentificationResult.Identified(new ProposedRecordingIdentity(title, artists, null), []);

    private static IdentificationResult LookupFailed() =>
        IdentificationResult.Unresolved("MusicBrainz could not be reached.", methods:
            [new IdentificationMethodResult("musicbrainz", IdentificationMethodStatus.Failed, "unreachable", [])]);

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite($"Data Source={_path};Pooling=False").Options);

    private ServiceProvider Services(Action<DownloadWorkerOptions>? configure = null)
    {
        _provider.SourcePath = _source;
        var settings = new DownloadWorkerOptions { RetryDelaySeconds = 30, MaxAttempts = 3 };
        configure?.Invoke(settings);
        return new ServiceCollection()
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .AddSingleton(Options.Create(settings))
            .AddSingleton<IAcquisitionProvider>(_provider)
            .AddSingleton<IRecordingIdentifier>(_identifier)
            .AddSingleton<IFileStorage>(_storage)
            .AddSingleton<ActiveUploadTracker>()
            .AddDbContext<MusicHubDbContext>(o => o.UseSqlite($"Data Source={_path};Pooling=False"))
            .AddScoped<IDownloadQueueService, DownloadQueueService>()
            .AddScoped<AcquisitionHistory>()
            .AddScoped<TrackAcquisition>()
            .AddScoped<AcquisitionFailover>()
            .AddScoped<FileIngestionService>()
            .AddScoped<AcquisitionHandoff>()
            .AddScoped<DownloadHandoffProcessor>()
            .BuildServiceProvider();
    }

    private static AcquisitionCandidate Candidate => new("slskd", "peer", @"Music\a\song.mp3", Bytes.Length, "mp3", 320);

    /// <summary>A request as the download leaves it: Downloaded, with its attempt record.</summary>
    private async Task<int> DownloadedRequestAsync()
    {
        await using var db = CreateDb();
        var queue = new DownloadQueueService(db, TimeProvider.System);
        var id = (await queue.EnqueueAsync(new EnqueueDownloadCommand(_trackId, Candidate), default)).Request!.Id;
        Assert.NotNull(await queue.ClaimNextAsync(default));
        var attempt = await new AcquisitionHistory(db, TimeProvider.System).BeginAsync(Candidate, _trackId, default);
        Assert.True(await queue.MarkDownloadedAsync(id, attempt.Id, default));
        return id;
    }

    private async Task<DownloadRequest> HandOffOneAsync(ServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var claim = await scope.ServiceProvider.GetRequiredService<IDownloadQueueService>().ClaimNextDownloadedAsync(ct);
        Assert.NotNull(claim);
        await scope.ServiceProvider.GetRequiredService<DownloadHandoffProcessor>().ProcessAsync(claim, ct);
        await using var db = CreateDb();
        return await db.DownloadRequests.AsNoTracking().SingleAsync(ct);
    }

    [Fact]
    public async Task The_wanted_recording_is_imported_linked_to_its_track_and_completes_the_request()
    {
        await DownloadedRequestAsync();
        _identifier.Script = () => Identified("Song", "Artist");
        using var services = Services();

        var request = await HandOffOneAsync(services);

        Assert.Equal(DownloadRequestStatus.Completed, request.Status);
        Assert.NotNull(request.FinishedAt);
        Assert.Equal(1, request.ProcessingAttempts);
        await using var db = CreateDb();
        var file = await db.Files.SingleAsync();
        Assert.Equal(FileStatus.Stored, file.Status);
        Assert.Equal(_trackId, file.TrackId);
        Assert.Equal("acquisition:slskd", file.Source);
        Assert.Equal(BytesHash, file.ContentHash);
        var download = await db.AcquisitionDownloads.SingleAsync();
        Assert.Equal(AcquisitionOutcome.Accepted, download.Outcome);
        Assert.Equal(file.Id, download.FileId);
        Assert.Equal(BytesHash, download.ContentHash);
        var attempt = await db.IdentificationOutcomes.SingleAsync();
        Assert.Equal(download.Id, attempt.DownloadId);
        Assert.Equal(file.Id, attempt.FileId);
        Assert.Equal(IdentificationDecision.Identified, attempt.Decision);
        var applied = await db.AppliedIdentifications.SingleAsync();
        Assert.Equal((file.Id, _trackId, IdentificationSource.Automatic), (applied.FileId, applied.TrackId, applied.Source));
        Assert.True(File.Exists(_source));
    }

    [Fact]
    public async Task A_confident_identity_of_another_track_is_rejected_and_stays_out_of_the_library()
    {
        await DownloadedRequestAsync();
        _identifier.Script = () => Identified("Other Song", "Artist");
        using var services = Services();

        var request = await HandOffOneAsync(services);

        Assert.Equal(DownloadRequestStatus.Failed, request.Status);
        Assert.Equal(AcquisitionFailure.IdentityRejected, request.Failure);
        Assert.Contains("not the wanted recording", request.Error);
        await using var db = CreateDb();
        Assert.Empty(await db.Files.ToListAsync());
        Assert.Empty(await db.AppliedIdentifications.ToListAsync());
        Assert.Empty(_storage.Keys);
        var download = await db.AcquisitionDownloads.SingleAsync();
        Assert.Equal(AcquisitionOutcome.Rejected, download.Outcome);
        Assert.Equal(BytesHash, download.ContentHash);
        Assert.Equal(IdentificationDecision.Mismatch, (await db.IdentificationOutcomes.SingleAsync()).Decision);
        Assert.True(File.Exists(_source));
        Assert.True(await new AcquisitionHistory(db, TimeProvider.System).WasRejectedAsync(Candidate, default));
    }

    [Fact]
    public async Task An_identity_without_evidence_is_rejected_not_imported()
    {
        await DownloadedRequestAsync();
        _identifier.Script = () => IdentificationResult.Unresolved("Nothing matched.", methods:
            [new IdentificationMethodResult("musicbrainz", IdentificationMethodStatus.NothingFound, "none", [])]);
        using var services = Services();

        var request = await HandOffOneAsync(services);

        Assert.Equal(AcquisitionFailure.IdentityRejected, request.Failure);
        await using var db = CreateDb();
        Assert.Empty(await db.Files.ToListAsync());
        Assert.Equal(IdentificationDecision.NoMatch, (await db.IdentificationOutcomes.SingleAsync()).Decision);
    }

    [Fact]
    public async Task A_versioned_track_is_never_confirmed_by_title_and_artists_alone()
    {
        await using (var db = CreateDb())
        {
            (await db.Tracks.SingleAsync()).Version = "Live";
            await db.SaveChangesAsync();
        }
        await DownloadedRequestAsync();
        _identifier.Script = () => Identified("Song", "Artist");
        using var services = Services();

        var request = await HandOffOneAsync(services);

        Assert.Equal(AcquisitionFailure.IdentityRejected, request.Failure);
        Assert.Contains("can't confirm a version", request.Error);
        await using var check = CreateDb();
        Assert.Empty(await check.Files.ToListAsync());
    }

    [Fact]
    public async Task A_method_that_could_not_answer_is_retried_with_a_wait_and_never_rejects_the_candidate()
    {
        await DownloadedRequestAsync();
        _identifier.Script = LookupFailed;
        using var services = Services();

        var request = await HandOffOneAsync(services);

        Assert.Equal(DownloadRequestStatus.Downloaded, request.Status);
        Assert.Equal(AcquisitionFailure.ProcessingFailed, request.Failure);
        Assert.NotNull(request.NextAttemptAt);
        Assert.True(request.NextAttemptAt > DateTimeOffset.UtcNow);
        await using var db = CreateDb();
        Assert.Equal(AcquisitionOutcome.Undecided, (await db.AcquisitionDownloads.SingleAsync()).Outcome);
        Assert.Empty(await db.Files.ToListAsync());
        // Waiting for the retry: not claimable before the time.
        Assert.Null(await new DownloadQueueService(db, TimeProvider.System).ClaimNextDownloadedAsync(default));
    }

    [Fact]
    public async Task A_transient_error_ends_the_request_as_failed_when_the_attempts_are_used_up_without_rejecting()
    {
        await DownloadedRequestAsync();
        _identifier.Script = () => throw new IOException("secret path /srv/x");
        using var services = Services(o => { o.MaxAttempts = 1; });

        var request = await HandOffOneAsync(services);

        Assert.Equal(DownloadRequestStatus.Failed, request.Status);
        Assert.Equal(AcquisitionFailure.ProcessingFailed, request.Failure);
        Assert.Contains("IOException", request.Error);
        Assert.DoesNotContain("secret", request.Error);
        await using var db = CreateDb();
        var download = await db.AcquisitionDownloads.SingleAsync();
        Assert.Equal(AcquisitionOutcome.Undecided, download.Outcome);
        Assert.Equal(AcquisitionDownloadState.Failed, download.State);
        Assert.Equal(DownloadStatus.Failed, download.Status);
        Assert.False(await new AcquisitionHistory(db, TimeProvider.System).WasRejectedAsync(Candidate, default));
    }

    [Fact]
    public async Task A_transient_error_is_retried_and_the_next_attempt_imports_the_file()
    {
        await DownloadedRequestAsync();
        var calls = 0;
        _identifier.Script = () => ++calls == 1 ? throw new IOException() : Identified("Song", "Artist");
        using var services = Services(o => o.RetryDelaySeconds = 0);

        var first = await HandOffOneAsync(services);
        Assert.Equal(DownloadRequestStatus.Downloaded, first.Status);
        var second = await HandOffOneAsync(services);

        Assert.Equal(DownloadRequestStatus.Completed, second.Status);
        Assert.Equal(2, second.ProcessingAttempts);
        await using var db = CreateDb();
        Assert.Equal(1, await db.Files.CountAsync());
    }

    [Fact]
    public async Task A_missing_source_file_fails_the_request_without_downloading_again()
    {
        await DownloadedRequestAsync();
        _provider.SourceExists = false;
        using var services = Services();

        var request = await HandOffOneAsync(services);

        Assert.Equal(DownloadRequestStatus.Failed, request.Status);
        Assert.Equal(AcquisitionFailure.FileNotAccessible, request.Failure);
        Assert.Equal(0, _identifier.Calls);
        await using var db = CreateDb();
        Assert.Empty(await db.Files.ToListAsync());
    }

    [Fact]
    public async Task A_track_that_is_gone_fails_the_request_instead_of_importing_an_unlinked_file()
    {
        await DownloadedRequestAsync();
        await using (var db = CreateDb())
        {
            db.Tracks.Remove(await db.Tracks.SingleAsync());
            await db.SaveChangesAsync();
        }
        using var services = Services();

        var request = await HandOffOneAsync(services);

        Assert.Equal(DownloadRequestStatus.Failed, request.Status);
        Assert.Equal(0, _identifier.Calls);
        await using var check = CreateDb();
        Assert.Empty(await check.Files.ToListAsync());
    }

    [Fact]
    public async Task Handing_off_again_after_a_crash_imports_nothing_twice()
    {
        await DownloadedRequestAsync();
        _identifier.Script = () => Identified("Song", "Artist");
        using var services = Services();
        await HandOffOneAsync(services);
        // The process stopped before the request was completed: it is Processing again after the restart recovery.
        await using (var db = CreateDb())
        {
            var request = await db.DownloadRequests.SingleAsync();
            request.Status = DownloadRequestStatus.Downloaded;
            await db.SaveChangesAsync();
        }

        var again = await HandOffOneAsync(services);

        Assert.Equal(DownloadRequestStatus.Completed, again.Status);
        Assert.Equal(1, _identifier.Calls);
        await using var check = CreateDb();
        Assert.Equal(1, await check.Files.CountAsync());
        Assert.Equal(1, await check.AppliedIdentifications.CountAsync());
        Assert.Equal(1, await check.IdentificationOutcomes.CountAsync());
    }

    [Fact]
    public async Task A_file_stored_by_an_earlier_try_that_never_got_linked_is_reused()
    {
        await DownloadedRequestAsync();
        await using (var db = CreateDb())
        {
            db.Files.Add(new FileRecord
            {
                OriginalFilename = "song.mp3", StorageLocation = "default", StorageKey = "library/aa/left-behind",
                Status = FileStatus.Stored, ContentHash = BytesHash, SizeBytes = Bytes.Length,
                Source = "acquisition:slskd", StartedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        _identifier.Script = () => Identified("Song", "Artist");
        using var services = Services();

        var request = await HandOffOneAsync(services);

        Assert.Equal(DownloadRequestStatus.Completed, request.Status);
        await using var check = CreateDb();
        var file = await check.Files.SingleAsync();
        Assert.Equal("library/aa/left-behind", file.StorageKey);
        Assert.Equal(_trackId, file.TrackId);
        Assert.Empty(_storage.Keys);
    }

    [Fact]
    public async Task A_rejection_that_was_recorded_before_a_crash_is_not_judged_again()
    {
        var id = await DownloadedRequestAsync();
        await using (var db = CreateDb())
        {
            var download = await db.AcquisitionDownloads.SingleAsync();
            await new AcquisitionHistory(db, TimeProvider.System).RejectedAsync(download, "Identified as another song.", default);
        }
        using var services = Services();

        var request = await HandOffOneAsync(services);

        Assert.Equal(id, request.Id);
        Assert.Equal(AcquisitionFailure.IdentityRejected, request.Failure);
        Assert.Equal("Identified as another song.", request.Error);
        Assert.Equal(0, _identifier.Calls);
    }

    [Fact]
    public async Task A_request_that_was_downloaded_before_the_restart_is_claimed_without_downloading()
    {
        var id = await DownloadedRequestAsync();
        await using var db = CreateDb();
        var queue = new DownloadQueueService(db, TimeProvider.System);

        var claim = await queue.ClaimNextDownloadedAsync(default);

        Assert.Equal(id, claim!.RequestId);
        Assert.Equal(1, claim.AttemptNumber);
        Assert.Equal(_trackId, claim.TrackId);
        Assert.Null(await queue.ClaimNextAsync(default));
        Assert.Equal(0, _provider.Located);
    }

    [Fact]
    public async Task Parallel_workers_never_claim_the_same_downloaded_request()
    {
        await DownloadedRequestAsync();

        var claims = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var db = CreateDb();
            return await new DownloadQueueService(db, TimeProvider.System).ClaimNextDownloadedAsync(default);
        }));

        Assert.Single(claims, c => c is not null);
    }

    [Fact]
    public async Task A_restart_puts_a_request_that_was_processing_back_to_downloaded()
    {
        var id = await DownloadedRequestAsync();
        await using var db = CreateDb();
        var queue = new DownloadQueueService(db, TimeProvider.System);
        Assert.NotNull(await queue.ClaimNextDownloadedAsync(default));

        Assert.Equal(1, await queue.RequeueInterruptedAsync(default));

        Assert.Equal(DownloadRequestStatus.Downloaded, (await queue.GetAsync(id, default))!.Status);
        Assert.NotNull(await queue.ClaimNextDownloadedAsync(default));
    }

    [Fact]
    public void Only_a_processing_request_completes_and_a_downloaded_one_cannot_skip_the_handoff()
    {
        Assert.False(DownloadRequestStatuses.CanTransition(DownloadRequestStatus.Downloaded, DownloadRequestStatus.Completed));
        Assert.True(DownloadRequestStatuses.CanTransition(DownloadRequestStatus.Downloaded, DownloadRequestStatus.Processing));
        Assert.True(DownloadRequestStatuses.CanTransition(DownloadRequestStatus.Processing, DownloadRequestStatus.Completed));
        Assert.True(DownloadRequestStatuses.CanTransition(DownloadRequestStatus.Processing, DownloadRequestStatus.Failed));
        Assert.True(DownloadRequestStatuses.CanTransition(DownloadRequestStatus.Processing, DownloadRequestStatus.Downloaded));
        Assert.Contains(DownloadRequestStatus.Processing, DownloadRequestStatuses.Active);
    }
}
