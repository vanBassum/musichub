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

public sealed class TrackAcquisitionTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeFileStorage _storage = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("musichub-acq").FullName;
    private readonly ScriptedProvider _provider = new();
    private readonly Dictionary<string, IdentificationResult> _identities = [];
    private readonly List<string> _identified = [];

    public TrackAcquisitionTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(_connection).Options);

    private sealed class ScriptedProvider : IAcquisitionProvider
    {
        public string Name => "slskd";
        public List<AcquisitionCandidate> Offers { get; } = [];
        public Dictionary<string, Func<AcquisitionDownloadOutcome>> Downloads { get; } = [];
        public List<string> Downloaded { get; } = [];
        public Exception? SearchFailure { get; set; }

        public Task<IReadOnlyList<AcquisitionCandidate>> SearchAsync(AcquisitionQuery query, CancellationToken ct) =>
            SearchFailure is { } failure ? throw failure : Task.FromResult<IReadOnlyList<AcquisitionCandidate>>(Offers.ToList());

        public Task<AcquisitionDownloadOutcome> DownloadAsync(AcquisitionCandidate candidate, CancellationToken ct)
        {
            Downloaded.Add(candidate.Path);
            return Task.FromResult(Downloads[candidate.Path]());
        }
    }

    private sealed class ByNameIdentifier(Dictionary<string, IdentificationResult> identities, List<string> seen) : IRecordingIdentifier
    {
        public Task<IdentificationResult> IdentifyAsync(Stream audio, string? fileNameHint, CancellationToken ct)
        {
            Assert.True(audio.CanSeek);
            seen.Add(fileNameHint!);
            return Task.FromResult(identities[fileNameHint!]);
        }
    }

    private static IdentificationResult Identified(string title, params string[] artists) =>
        IdentificationResult.Identified(
            new ProposedRecordingIdentity(title, artists, null),
            [new("acoustid", "fingerprint match")],
            [new("acoustid", IdentificationMethodStatus.Found, "one hit", [$"\"{title}\""])]);

    private async Task<int> AddTrackAsync(string title = "Wonderwall", string artist = "Oasis", string? version = null)
    {
        using var db = CreateDb();
        var track = new Track { Title = title, Version = version, CreatedAt = DateTimeOffset.UtcNow };
        track.Artists.Add(new TrackArtist { Artist = new Artist { Name = artist, CreatedAt = DateTimeOffset.UtcNow } });
        db.Tracks.Add(track);
        await db.SaveChangesAsync();
        return track.Id;
    }

    /// <summary>Offers a candidate whose download is a real file of those bytes, and says what identification makes of it.</summary>
    private void Offer(string path, byte[] bytes, IdentificationResult? identity, int bitrate = 320, string source = "peer")
    {
        var name = Path.GetFileName(path.Replace('\\', '/'));
        var local = Path.Combine(_folder, Guid.NewGuid() + ".mp3");
        File.WriteAllBytes(local, bytes);
        _provider.Offers.Add(new AcquisitionCandidate("slskd", source, path, bytes.Length, "mp3", bitrate, HasFreeSlot: true, QueueLength: 0));
        _provider.Downloads[path] = () => AcquisitionDownloadOutcome.Completed("slskd", new LocalAcquiredFile(name, local));
        if (identity is not null)
            _identities[name] = identity;
    }

    private static byte[] Bytes(int seed) => Enumerable.Range(0, 3000).Select(i => (byte)((i * seed) % 255 + 1)).ToArray();

    private async Task<TrackAcquisitionResult> RunAsync(int trackId, string execution = "7")
    {
        using var db = CreateDb();
        var acquisition = new TrackAcquisition(
            db, new AcquisitionHistory(db, TimeProvider.System), _provider, new ByNameIdentifier(_identities, _identified),
            new AcquisitionHandoff(new FileIngestionService(db, _storage, new ActiveUploadTracker())),
            Options.Create(new MissingMusicOptions { MaxDownloadsPerTrack = 3 }), TimeProvider.System,
            NullLogger<TrackAcquisition>.Instance);
        return await acquisition.RunAsync(trackId, execution, CancellationToken.None);
    }

    [Fact]
    public async Task An_identified_candidate_is_imported_linked_and_recorded_with_hashes_and_provenance()
    {
        var trackId = await AddTrackAsync();
        var bytes = Bytes(3);
        Offer(@"Oasis\Wonderwall.mp3", bytes, Identified("Wonderwall", "Oasis"));

        var result = await RunAsync(trackId);

        Assert.Equal(TrackAcquisitionOutcome.Imported, result.Outcome);
        using var db = CreateDb();
        var file = await db.Files.SingleAsync();
        Assert.Equal(trackId, file.TrackId);
        Assert.Equal(FileStatus.Stored, file.Status);
        Assert.Equal("acquisition:slskd", file.Source);
        var download = await db.AcquisitionDownloads.SingleAsync();
        Assert.Equal(DownloadStatus.Imported, download.Status);
        Assert.Equal(file.Id, download.FileId);
        Assert.Equal(file.ContentHash, download.ContentHash);
        Assert.Equal(Mp3AudioPayload.Version, download.AudioHashVersion);
        Assert.NotNull(download.AudioHashStatus);
        Assert.Equal(("slskd", "peer", @"Oasis\Wonderwall.mp3"), (download.Provider, download.Source, download.RemotePath));
        Assert.Equal("7", download.JobExecutionId);

        var attempt = await db.IdentificationOutcomes.SingleAsync();
        Assert.Equal(download.Id, attempt.DownloadId);
        Assert.Null(attempt.FileId);
        Assert.Equal(IdentificationDecision.Identified, attempt.Decision);
        Assert.Equal(trackId, attempt.TrackId);
        Assert.Equal("acoustid", Assert.Single(attempt.ReadMethods()).Source);
        Assert.False(string.IsNullOrEmpty(attempt.PipelineVersion));
        var applied = await db.AppliedIdentifications.SingleAsync();
        Assert.Equal((file.Id, trackId, attempt.Id), (applied.FileId, applied.TrackId, applied.AttemptId));
    }

    [Fact]
    public async Task A_file_stored_by_an_import_that_stopped_before_linking_is_linked_not_stored_again()
    {
        var trackId = await AddTrackAsync();
        var bytes = Bytes(3);
        Offer(@"Oasis\Wonderwall.mp3", bytes, Identified("Wonderwall", "Oasis"));
        using (var seed = CreateDb())
        {
            seed.Files.Add(new FileRecord
            {
                OriginalFilename = "Wonderwall.mp3", StorageLocation = "default", StorageKey = "library/ab/orphan",
                Status = FileStatus.Stored, Source = AcquisitionHandoff.SourceOf("slskd"), StartedAt = DateTimeOffset.UtcNow,
                ContentHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), SizeBytes = bytes.Length,
            });
            await seed.SaveChangesAsync();
        }

        var result = await RunAsync(trackId);

        Assert.Equal(TrackAcquisitionOutcome.Imported, result.Outcome);
        using var db = CreateDb();
        var file = await db.Files.SingleAsync();
        Assert.Equal(trackId, file.TrackId);
        Assert.Equal(file.Id, (await db.AcquisitionDownloads.SingleAsync()).FileId);
    }

    [Fact]
    public async Task A_candidate_identified_as_another_recording_never_enters_the_library_and_is_not_downloaded_again()
    {
        var trackId = await AddTrackAsync();
        Offer(@"Oasis\Wonderwall.mp3", Bytes(3), Identified("Wonderwall", "Mike Flowers Pops"));

        var first = await RunAsync(trackId, "1");
        var second = await RunAsync(trackId, "2");

        Assert.Equal(TrackAcquisitionOutcome.NoAcceptableCandidate, first.Outcome);
        Assert.Equal(TrackAcquisitionOutcome.NothingFound, second.Outcome);
        Assert.Single(_provider.Downloaded);
        using var db = CreateDb();
        Assert.Empty(await db.Files.ToListAsync());
        var download = await db.AcquisitionDownloads.SingleAsync();
        Assert.Equal(DownloadStatus.Rejected, download.Status);
        Assert.NotNull(download.ContentHash);
        Assert.Contains("Mike Flowers Pops", download.Reason);
        var attempt = await db.IdentificationOutcomes.SingleAsync();
        Assert.Equal(IdentificationDecision.Mismatch, attempt.Decision);
        Assert.Equal(download.Id, attempt.DownloadId);
    }

    [Fact]
    public async Task An_unidentified_candidate_is_rejected_with_the_methods_that_were_tried()
    {
        var trackId = await AddTrackAsync();
        Offer(@"Oasis\Wonderwall.mp3", Bytes(3), IdentificationResult.Unresolved("no match", [],
            [new IdentificationMethodResult("acoustid", IdentificationMethodStatus.NothingFound, "none", [])]));

        var result = await RunAsync(trackId);

        Assert.Equal(TrackAcquisitionOutcome.NoAcceptableCandidate, result.Outcome);
        using var db = CreateDb();
        Assert.Empty(await db.Files.ToListAsync());
        Assert.Equal(IdentificationDecision.NoMatch, (await db.IdentificationOutcomes.SingleAsync()).Decision);
    }

    [Fact]
    public async Task A_lower_bitrate_is_accepted_when_the_better_candidate_is_the_wrong_recording()
    {
        var trackId = await AddTrackAsync();
        Offer(@"A\Oasis - Wonderwall.mp3", Bytes(3), Identified("Wonderwall", "Somebody Else"), bitrate: 320);
        Offer(@"B\Oasis_Wonderwall.mp3", Bytes(5), Identified("Wonderwall", "Oasis"), bitrate: 128, source: "other");

        var result = await RunAsync(trackId);

        Assert.Equal(TrackAcquisitionOutcome.Imported, result.Outcome);
        using var db = CreateDb();
        Assert.Equal([DownloadStatus.Rejected, DownloadStatus.Imported], (await db.AcquisitionDownloads.OrderBy(d => d.Id).ToListAsync()).Select(d => d.Status));
        Assert.Equal(128, (await db.AcquisitionDownloads.SingleAsync(d => d.Outcome == AcquisitionOutcome.Accepted)).BitrateKbps);
    }

    [Fact]
    public async Task The_same_bytes_rejected_before_are_rejected_again_without_identifying_them()
    {
        var trackId = await AddTrackAsync();
        var bytes = Bytes(3);
        Offer(@"A\Oasis - Wonderwall.mp3", bytes, Identified("Wonderwall", "Somebody Else"), bitrate: 320);
        Offer(@"B\Oasis_Wonderwall.mp3", bytes, Identified("Wonderwall", "Oasis"), bitrate: 128, source: "reupload");

        var result = await RunAsync(trackId);

        Assert.Equal(TrackAcquisitionOutcome.NoAcceptableCandidate, result.Outcome);
        Assert.Single(_identified);
        using var db = CreateDb();
        var second = await db.AcquisitionDownloads.OrderBy(d => d.Id).LastAsync();
        Assert.Equal(DownloadStatus.Rejected, second.Status);
        Assert.Contains("already rejected", second.Reason);
        Assert.NotNull(second.ContentHash);
    }

    [Fact]
    public async Task A_failed_download_is_recorded_and_may_be_tried_again_later()
    {
        var trackId = await AddTrackAsync();
        Offer(@"Oasis\Wonderwall.mp3", Bytes(3), Identified("Wonderwall", "Oasis"));
        var good = _provider.Downloads[@"Oasis\Wonderwall.mp3"];
        _provider.Downloads[@"Oasis\Wonderwall.mp3"] = () =>
            AcquisitionDownloadOutcome.Failed("slskd", AcquisitionFailure.TransferFailed, "The peer went away.");

        var first = await RunAsync(trackId, "1");
        _provider.Downloads[@"Oasis\Wonderwall.mp3"] = good;
        var second = await RunAsync(trackId, "2");

        Assert.Equal(TrackAcquisitionOutcome.NoAcceptableCandidate, first.Outcome);
        Assert.Equal(TrackAcquisitionOutcome.Imported, second.Outcome);
        using var db = CreateDb();
        Assert.Equal([DownloadStatus.Failed, DownloadStatus.Imported], (await db.AcquisitionDownloads.OrderBy(d => d.Id).ToListAsync()).Select(d => d.Status));
        Assert.Contains("peer went away", (await db.AcquisitionDownloads.OrderBy(d => d.Id).FirstAsync()).Reason);
    }

    [Fact]
    public async Task A_track_with_a_version_is_never_confirmed_by_an_identification_without_one()
    {
        var trackId = await AddTrackAsync(version: "Live at Wembley");
        Offer(@"Oasis\Wonderwall (Live at Wembley).mp3", Bytes(3), Identified("Wonderwall", "Oasis"));

        var result = await RunAsync(trackId);

        Assert.Equal(TrackAcquisitionOutcome.NoAcceptableCandidate, result.Outcome);
        using var db = CreateDb();
        Assert.Empty(await db.Files.ToListAsync());
        Assert.Equal(IdentificationDecision.InsufficientEvidence, (await db.IdentificationOutcomes.SingleAsync()).Decision);
    }

    [Fact]
    public async Task Nothing_is_downloaded_when_no_plausible_mp3_is_offered_or_the_provider_is_down()
    {
        var trackId = await AddTrackAsync();
        _provider.Offers.Add(new AcquisitionCandidate("slskd", "peer", @"Blur\Song 2.mp3", 100, "mp3", 320));

        Assert.Equal(TrackAcquisitionOutcome.NothingFound, (await RunAsync(trackId)).Outcome);

        _provider.SearchFailure = new AcquisitionUnavailableException("slskd could not be reached");
        var down = await RunAsync(trackId);
        Assert.Equal(TrackAcquisitionOutcome.ProviderUnavailable, down.Outcome);
        Assert.Empty(_provider.Downloaded);
        using var db = CreateDb();
        Assert.Empty(await db.AcquisitionDownloads.ToListAsync());
    }

    [Fact]
    public async Task A_track_that_has_a_stored_file_is_skipped()
    {
        var trackId = await AddTrackAsync();
        using (var db = CreateDb())
        {
            db.Files.Add(new FileRecord
            {
                OriginalFilename = "a.mp3", StorageLocation = "default", StorageKey = "library/aa/x", Status = FileStatus.Stored,
                Source = "upload", StartedAt = DateTimeOffset.UtcNow, TrackId = trackId,
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(TrackAcquisitionOutcome.Skipped, (await RunAsync(trackId)).Outcome);
        Assert.Empty(_provider.Offers);
    }

    [Fact]
    public async Task A_cancelled_run_marks_the_download_cancelled_so_it_can_be_tried_again()
    {
        var trackId = await AddTrackAsync();
        Offer(@"Oasis\Wonderwall.mp3", Bytes(3), Identified("Wonderwall", "Oasis"));
        using var cts = new CancellationTokenSource();
        _provider.Downloads[@"Oasis\Wonderwall.mp3"] = () =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };

        using (var db = CreateDb())
        {
            var acquisition = new TrackAcquisition(
                db, new AcquisitionHistory(db, TimeProvider.System), _provider, new ByNameIdentifier(_identities, _identified),
                new AcquisitionHandoff(new FileIngestionService(db, _storage, new ActiveUploadTracker())),
                Options.Create(new MissingMusicOptions()), TimeProvider.System, NullLogger<TrackAcquisition>.Instance);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquisition.RunAsync(trackId, "1", cts.Token));
        }

        using var read = CreateDb();
        Assert.Equal(DownloadStatus.Cancelled, (await read.AcquisitionDownloads.SingleAsync()).Status);
        Assert.Empty(await read.Files.ToListAsync());
    }

    [Fact]
    public async Task A_run_goes_through_every_missing_track_and_summarizes_each()
    {
        var found = await AddTrackAsync();
        var absent = await AddTrackAsync("Song 2", "Blur");
        Offer(@"Oasis\Wonderwall.mp3", Bytes(3), Identified("Wonderwall", "Oasis"));

        var services = new ServiceCollection();
        services.AddSingleton(_connection);
        services.AddDbContext<MusicHubDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<IFileStorage>(_storage);
        services.AddSingleton<ActiveUploadTracker>();
        services.AddSingleton<IAcquisitionProvider>(_provider);
        services.AddSingleton<IRecordingIdentifier>(new ByNameIdentifier(_identities, _identified));
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.Configure<MissingMusicOptions>(o => o.MaxConcurrentTracks = 2);
        services.AddScoped<FileIngestionService>();
        services.AddScoped<AcquisitionHandoff>();
        services.AddScoped<AcquisitionHistory>();
        services.AddScoped<TrackAcquisition>();
        services.AddScoped<MissingMusicAcquisition>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        int? total = null;

        var summary = await scope.ServiceProvider.GetRequiredService<MissingMusicAcquisition>()
            .RunAsync("9", count => total = count, CancellationToken.None);

        Assert.Equal(2, total);
        Assert.Equal(2, summary.Examined);
        Assert.Equal(1, summary.Imported);
        Assert.Equal(1, summary.NothingFound);
        Assert.Equal([found, absent], summary.Tracks.Select(t => t.TrackId));
        Assert.Equal(TrackAcquisitionOutcome.Imported, summary.Tracks[0].Outcome);
        Assert.Equal("Oasis", summary.Tracks[0].Artists);
    }
}
