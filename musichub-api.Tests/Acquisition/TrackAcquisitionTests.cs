using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    private async Task<TrackDiscovery> DiscoverAsync(int trackId)
    {
        using var db = CreateDb();
        var acquisition = new TrackAcquisition(db, new AcquisitionHistory(db, TimeProvider.System), _provider);
        return await acquisition.DiscoverAsync(trackId, CancellationToken.None);
    }

    [Fact]
    public async Task Nothing_is_found_when_no_plausible_mp3_is_offered_or_the_provider_is_down()
    {
        var trackId = await AddTrackAsync();
        _provider.Offers.Add(new AcquisitionCandidate("slskd", "peer", @"Blur\Song 2.mp3", 100, "mp3", 320));

        Assert.Equal(TrackAcquisitionOutcome.NothingFound, (await DiscoverAsync(trackId)).Stopped!.Outcome);

        _provider.SearchFailure = new AcquisitionUnavailableException("slskd could not be reached");
        Assert.Equal(TrackAcquisitionOutcome.ProviderUnavailable, (await DiscoverAsync(trackId)).Stopped!.Outcome);
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

        Assert.Equal(TrackAcquisitionOutcome.Skipped, (await DiscoverAsync(trackId)).Stopped!.Outcome);
        Assert.Empty(_provider.Offers);
    }

    [Fact]
    public async Task A_run_enqueues_the_best_candidate_of_every_missing_track_without_downloading_and_is_idempotent()
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
        services.AddScoped<IDownloadQueueService, DownloadQueueService>();
        services.AddScoped<TrackAcquisition>();
        services.AddScoped<MissingMusicAcquisition>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        int? total = null;

        var summary = await scope.ServiceProvider.GetRequiredService<MissingMusicAcquisition>()
            .RunAsync("9", count => total = count, CancellationToken.None);

        Assert.Equal(2, total);
        Assert.Equal(2, summary.Examined);
        Assert.Equal(1, summary.Enqueued);
        Assert.Equal(0, summary.AlreadyQueued);
        Assert.Equal(1, summary.NothingFound);
        Assert.Equal([found, absent], summary.Tracks.Select(t => t.TrackId));
        Assert.Equal(TrackAcquisitionOutcome.Enqueued, summary.Tracks[0].Outcome);
        Assert.Equal("Oasis", summary.Tracks[0].Artists);
        Assert.Empty(_provider.Downloaded);
        using (var db = CreateDb())
        {
            var request = await db.DownloadRequests.SingleAsync();
            Assert.Equal(found, request.TrackId);
            Assert.Equal(DownloadRequestStatus.Queued, request.Status);
            Assert.Equal("9", request.JobExecutionId);
            Assert.Empty(await db.Files.ToListAsync());
        }

        var again = await scope.ServiceProvider.GetRequiredService<MissingMusicAcquisition>()
            .RunAsync("10", null, CancellationToken.None);

        Assert.Equal(0, again.Enqueued);
        Assert.Equal(1, again.AlreadyQueued);
        Assert.Equal(1, again.NothingFound);
        using var after = CreateDb();
        Assert.Single(await after.DownloadRequests.ToListAsync());
    }

    [Fact]
    public async Task A_candidate_rejected_before_is_not_enqueued_and_the_next_best_is()
    {
        var trackId = await AddTrackAsync();
        Offer(@"Oasis\Wonderwall.mp3", Bytes(3), Identified("Wonderwall", "Oasis"), bitrate: 320);
        Offer(@"Oasis\Wonderwall (b).mp3", Bytes(5), Identified("Wonderwall", "Oasis"), bitrate: 256);
        var first = _provider.Offers[0];
        using (var db = CreateDb())
        {
            var history = new AcquisitionHistory(db, TimeProvider.System);
            await history.RejectedAsync(await history.BeginAsync(first, trackId, CancellationToken.None), "No.", CancellationToken.None);
        }

        var discovery = await DiscoverAsync(trackId);

        Assert.Null(discovery.Stopped);
        Assert.Equal(@"Oasis\Wonderwall (b).mp3", discovery.Fresh[0].Path);
        Assert.Empty(_provider.Downloaded);
    }
}
