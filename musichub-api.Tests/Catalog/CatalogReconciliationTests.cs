using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;
using MusicHub.Api.Identification;
using MusicHub.Api.Ingestion;
using MusicHub.Api.Tests.Ingestion;

namespace MusicHub.Api.Tests.Catalog;

public sealed class CatalogReconciliationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeFileStorage _storage = new();
    private readonly Dictionary<string, Func<IdentificationResult>> _identities = [];
    private readonly ListLogger _log = new();

    public CatalogReconciliationTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(_connection).Options);

    /// <summary>Identifies by file name: each test says what a file name is.</summary>
    private sealed class ByNameIdentifier(Dictionary<string, Func<IdentificationResult>> identities) : IRecordingIdentifier
    {
        public int Calls { get; private set; }

        public Task<IdentificationResult> IdentifyAsync(Stream audio, string? fileNameHint, CancellationToken ct)
        {
            Calls++;
            Assert.True(audio.CanSeek);
            return Task.FromResult(identities[fileNameHint!]());
        }
    }

    private sealed class ListLogger : ILogger<CatalogReconciliation>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static IdentificationResult Identified(string title, params string[] artists) =>
        IdentificationResult.Identified(new ProposedRecordingIdentity(title, artists, null), [new("file-metadata", "tags")]);

    private async Task<int> AddFileAsync(string name, FileStatus status = FileStatus.Stored, int? trackId = null)
    {
        using var db = CreateDb();
        var record = new FileRecord
        {
            OriginalFilename = name,
            StorageLocation = "default",
            StorageKey = $"library/aa/{Guid.NewGuid()}",
            Status = status,
            Source = "upload",
            StartedAt = DateTimeOffset.UtcNow,
            TrackId = trackId,
        };
        db.Files.Add(record);
        await db.SaveChangesAsync();
        await _storage.PutAsync(record.StorageKey, new MemoryStream([1, 2, 3]), null, CancellationToken.None);
        return record.Id;
    }

    private async Task<CatalogReconciliationSummary> RunAsync(ByNameIdentifier? identifier = null, string? executionId = null)
    {
        using var db = CreateDb();
        var service = new CatalogReconciliation(
            db, _storage, identifier ?? new ByNameIdentifier(_identities), TimeProvider.System, _log);
        return await service.RunAsync(executionId, CancellationToken.None);
    }

    private async Task<List<IdentificationOutcome>> OutcomesAsync(string executionId)
    {
        using var db = CreateDb();
        return await db.IdentificationOutcomes.Where(o => o.JobExecutionId == executionId).OrderBy(o => o.Id).ToListAsync();
    }

    private static IdentificationMethodResult Method(string source, IdentificationMethodStatus status, params string[] candidates) =>
        new(source, status, $"{source} note", candidates);

    private static IdentificationResult Unresolved(string reason, params IdentificationMethodResult[] methods) =>
        IdentificationResult.Unresolved(reason, [], methods);

    [Fact]
    public async Task A_confidently_identified_file_is_linked_to_a_new_track_with_its_artists()
    {
        _identities["a.mp3"] = () => Identified("Song", "Artist One", "Artist Two");
        var id = await AddFileAsync("a.mp3");

        var summary = await RunAsync();

        Assert.Equal(new CatalogReconciliationSummary(1, 1, 1, 0, 0, 0), summary);
        using var db = CreateDb();
        var file = await db.Files.SingleAsync(f => f.Id == id);
        var track = await db.Tracks.Include(t => t.Artists).ThenInclude(a => a.Artist).SingleAsync();
        Assert.Equal(track.Id, file.TrackId);
        Assert.Equal("Song", track.Title);
        Assert.Null(track.Version);
        Assert.Equal(["Artist One", "Artist Two"], track.Artists.Select(a => a.Artist.Name).Order());
        Assert.Equal(FileStatus.Stored, file.Status);
        Assert.Contains(_log.Entries, e => e.Message.Contains($"File {id} linked to new track"));
    }

    [Fact]
    public async Task A_file_of_an_existing_recording_is_linked_to_that_track_without_duplicates()
    {
        _identities["a.mp3"] = () => Identified("Song", "Artist");
        _identities["b.flac"] = () => Identified("  song ", "ARTIST");
        await AddFileAsync("a.mp3");
        await AddFileAsync("b.flac");

        var summary = await RunAsync();

        Assert.Equal(new CatalogReconciliationSummary(2, 2, 1, 0, 0, 0), summary);
        using var db = CreateDb();
        Assert.Equal(1, await db.Tracks.CountAsync());
        Assert.Equal(1, await db.Artists.CountAsync());
        Assert.Equal(1, await db.TrackArtists.CountAsync());
        Assert.Equal(1, await db.Files.Select(f => f.TrackId).Distinct().CountAsync());
    }

    [Fact]
    public async Task Versions_stay_distinct_tracks_and_artists_are_reused()
    {
        using (var db = CreateDb())
        {
            var artist = new Artist { Name = "Artist", CreatedAt = DateTimeOffset.UtcNow };
            db.Tracks.Add(new Track
            {
                Title = "Song", Version = "Radio edit", CreatedAt = DateTimeOffset.UtcNow,
                Artists = [new TrackArtist { Artist = artist }],
            });
            await db.SaveChangesAsync();
        }
        _identities["live.mp3"] = () => Identified("Song (Live)", "Artist");
        _identities["plain.mp3"] = () => Identified("Song", "Artist");
        await AddFileAsync("live.mp3");
        await AddFileAsync("plain.mp3");

        var summary = await RunAsync();

        Assert.Equal(2, summary.TracksCreated);
        using var check = CreateDb();
        Assert.Equal(3, await check.Tracks.CountAsync());
        Assert.Equal(1, await check.Artists.CountAsync());
    }

    [Fact]
    public async Task A_track_with_other_artists_is_not_reused()
    {
        _identities["a.mp3"] = () => Identified("Song", "Artist");
        _identities["b.mp3"] = () => Identified("Song", "Someone Else");
        await AddFileAsync("a.mp3");
        await AddFileAsync("b.mp3");

        await RunAsync();

        using var db = CreateDb();
        Assert.Equal(2, await db.Tracks.CountAsync());
    }

    [Fact]
    public async Task An_unresolved_file_stays_stored_and_unlinked_and_the_reason_is_logged()
    {
        _identities["x.mp3"] = () => IdentificationResult.Unresolved("evidence is contradictory");
        var id = await AddFileAsync("x.mp3");

        var summary = await RunAsync();

        Assert.Equal(new CatalogReconciliationSummary(1, 0, 0, 1, 0, 0), summary);
        using var db = CreateDb();
        var file = await db.Files.SingleAsync(f => f.Id == id);
        Assert.Null(file.TrackId);
        Assert.Equal(FileStatus.Stored, file.Status);
        Assert.Equal(0, await db.Tracks.CountAsync());
        Assert.Equal(0, await db.Artists.CountAsync());
        Assert.Contains(_log.Entries, e => e.Message.Contains("evidence is contradictory"));
    }

    [Fact]
    public async Task An_identity_without_title_or_artist_is_treated_as_unresolved()
    {
        _identities["a.mp3"] = () => Identified(" ", "Artist");
        _identities["b.mp3"] = () => Identified("Song");
        await AddFileAsync("a.mp3");
        await AddFileAsync("b.mp3");

        var summary = await RunAsync();

        Assert.Equal(2, summary.Unresolved);
        using var db = CreateDb();
        Assert.Equal(0, await db.Tracks.CountAsync());
    }

    [Fact]
    public async Task One_failing_file_does_not_stop_the_batch_and_the_error_is_logged()
    {
        _identities["bad.mp3"] = () => throw new InvalidOperationException("provider down");
        _identities["good.mp3"] = () => Identified("Song", "Artist");
        var bad = await AddFileAsync("bad.mp3");
        await AddFileAsync("good.mp3");

        var summary = await RunAsync();

        Assert.Equal(new CatalogReconciliationSummary(2, 1, 1, 0, 0, 1), summary);
        using var db = CreateDb();
        Assert.Null((await db.Files.SingleAsync(f => f.Id == bad)).TrackId);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains($"file {bad} failed"));
    }

    [Fact]
    public async Task A_file_whose_object_is_missing_fails_alone()
    {
        _identities["good.mp3"] = () => Identified("Song", "Artist");
        var missing = await AddFileAsync("gone.mp3");
        await AddFileAsync("good.mp3");
        using (var db = CreateDb())
        {
            var record = await db.Files.SingleAsync(f => f.Id == missing);
            await _storage.DeleteAsync(record.StorageKey, CancellationToken.None);
        }

        var summary = await RunAsync();

        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Linked);
    }

    [Fact]
    public async Task Only_stored_files_without_a_track_are_examined()
    {
        _identities["a.mp3"] = () => Identified("Song", "Artist");
        int trackId;
        using (var db = CreateDb())
        {
            var track = new Track { Title = "Existing", CreatedAt = DateTimeOffset.UtcNow };
            db.Tracks.Add(track);
            await db.SaveChangesAsync();
            trackId = track.Id;
        }
        await AddFileAsync("linked.mp3", trackId: trackId);
        await AddFileAsync("failed.mp3", FileStatus.Failed);
        await AddFileAsync("uploading.mp3", FileStatus.Uploading);
        await AddFileAsync("deleting.mp3", FileStatus.Deleting);
        await AddFileAsync("a.mp3");
        var identifier = new ByNameIdentifier(_identities);

        var summary = await RunAsync(identifier);

        Assert.Equal(1, summary.Examined);
        Assert.Equal(1, identifier.Calls);
    }

    [Fact]
    public async Task A_rerun_changes_nothing_and_only_unresolved_files_are_tried_again()
    {
        _identities["a.mp3"] = () => Identified("Song", "Artist");
        _identities["x.mp3"] = () => IdentificationResult.Unresolved("not sure");
        await AddFileAsync("a.mp3");
        await AddFileAsync("x.mp3");
        await RunAsync();

        var identifier = new ByNameIdentifier(_identities);
        var second = await RunAsync(identifier);

        Assert.Equal(new CatalogReconciliationSummary(1, 0, 0, 1, 0, 0), second);
        Assert.Equal(1, identifier.Calls);
        using var db = CreateDb();
        Assert.Equal(1, await db.Tracks.CountAsync());
        Assert.Equal(1, await db.TrackArtists.CountAsync());
    }

    [Fact]
    public async Task Concurrent_runs_create_one_track_and_link_each_file_once()
    {
        _identities["a.mp3"] = () => Identified("Song", "Artist");
        _identities["b.mp3"] = () => Identified("Song", "Artist");
        await AddFileAsync("a.mp3");
        await AddFileAsync("b.mp3");

        var summaries = await Task.WhenAll(RunAsync(), RunAsync());

        Assert.Equal(2, summaries.Sum(s => s.Linked));
        using var db = CreateDb();
        Assert.Equal(1, await db.Tracks.CountAsync());
        Assert.Equal(1, await db.Artists.CountAsync());
        Assert.All(await db.Files.ToListAsync(), f => Assert.NotNull(f.TrackId));
    }

    [Fact]
    public async Task A_file_deleted_while_being_identified_is_skipped_and_leaves_no_track()
    {
        var id = await AddFileAsync("a.mp3");
        _identities["a.mp3"] = () =>
        {
            using var other = CreateDb();
            other.Files.Where(f => f.Id == id).ExecuteDelete();
            return Identified("Song", "Artist");
        };

        var summary = await RunAsync();

        Assert.Equal(1, summary.Skipped);
        using var db = CreateDb();
        Assert.Equal(0, await db.Tracks.CountAsync());
    }

    [Fact]
    public async Task Each_examined_file_gets_one_outcome_that_says_how_it_ended()
    {
        const string F = "found", N = "nothing", C = "conflict", U = "unconfigured", X = "failed", B = "boom";
        _identities["linked.mp3"] = () => IdentificationResult.Identified(
            new ProposedRecordingIdentity("Song", ["Artist"], null), [new("musicbrainz", "x")],
            [Method("musicbrainz", IdentificationMethodStatus.Found, "\"Song\" by Artist")]);
        _identities[N] = () => Unresolved("No identification tool produced a candidate.",
            Method("musicbrainz", IdentificationMethodStatus.NothingFound), Method("acoustid", IdentificationMethodStatus.Unconfigured));
        _identities[C] = () => Unresolved("The evidence names 2 different recordings.",
            Method("file-metadata", IdentificationMethodStatus.Found, "\"A\" by X"), Method("musicbrainz", IdentificationMethodStatus.Found, "\"B\" by Y"));
        _identities[U] = () => Unresolved("No identification tool produced a candidate.",
            Method("musicbrainz", IdentificationMethodStatus.Unconfigured), Method("acoustid", IdentificationMethodStatus.NotRun));
        _identities[X] = () => Unresolved("No identification tool produced a candidate.",
            Method("musicbrainz", IdentificationMethodStatus.Failed), Method("acoustid", IdentificationMethodStatus.NothingFound));
        _identities[B] = () => throw new InvalidOperationException("secret connection string");
        _identities[F] = () => Unresolved("The file is not readable audio.");
        foreach (var name in new[] { "linked.mp3", N, C, U, X, B, F })
            await AddFileAsync(name);

        var summary = await RunAsync(executionId: "42");

        Assert.Equal(new CatalogReconciliationSummary(7, 1, 1, 5, 0, 1), summary);
        var outcomes = await OutcomesAsync("42");
        Assert.Equal(7, outcomes.Count);
        IdentificationOutcome Of(string name) => outcomes.Single(o => o.FileName == name);
        Assert.Equal(IdentificationDecision.Identified, Of("linked.mp3").Decision);
        Assert.NotNull(Of("linked.mp3").TrackId);
        Assert.Equal(IdentificationDecision.NoMatch, Of(N).Decision);
        Assert.Equal(IdentificationDecision.InsufficientEvidence, Of(C).Decision);
        Assert.Equal(IdentificationDecision.NoMethodRan, Of(U).Decision);
        Assert.Equal(IdentificationDecision.MethodFailed, Of(X).Decision);
        Assert.Equal(IdentificationDecision.NoMethodRan, Of(F).Decision);
        Assert.Equal(IdentificationDecision.Error, Of(B).Decision);
        Assert.DoesNotContain("secret", Of(B).Reason);
        Assert.Contains("\"status\":\"Failed\"", Of(X).MethodsJson);
        Assert.Contains("\"status\":\"Unconfigured\"", Of(N).MethodsJson);
        Assert.Contains("\"\\u0022Song\\u0022 by Artist\"", Of("linked.mp3").MethodsJson);
    }

    [Fact]
    public async Task Repeated_executions_keep_their_own_outcomes()
    {
        _identities["x.mp3"] = () => Unresolved("nothing", Method("musicbrainz", IdentificationMethodStatus.NothingFound));
        await AddFileAsync("x.mp3");

        await RunAsync(executionId: "1");
        await RunAsync(executionId: "2");
        await RunAsync();

        Assert.Single(await OutcomesAsync("1"));
        Assert.Single(await OutcomesAsync("2"));
        using var db = CreateDb();
        Assert.Equal(2, await db.IdentificationOutcomes.CountAsync());
    }

    [Fact]
    public async Task Outcomes_older_than_the_retention_are_pruned_and_a_deleted_file_takes_its_outcomes_along()
    {
        _identities["old.mp3"] = () => Unresolved("nothing");
        _identities["new.mp3"] = () => Unresolved("nothing");
        var old = await AddFileAsync("old.mp3");
        await RunAsync(executionId: "1");
        using (var db = CreateDb())
        {
            await db.IdentificationOutcomes.ExecuteUpdateAsync(s => s.SetProperty(o => o.RecordedAt, DateTimeOffset.UtcNow.AddDays(-30)));
        }
        await AddFileAsync("new.mp3");
        await RunAsync(executionId: "2");

        using (var db = CreateDb())
        {
            var service = new CatalogReconciliation(db, _storage, new ByNameIdentifier(_identities), TimeProvider.System, _log);
            Assert.Equal(1, await service.PruneOutcomesAsync(TimeSpan.FromDays(7), CancellationToken.None));
        }
        Assert.Empty(await OutcomesAsync("1"));
        Assert.Equal(2, (await OutcomesAsync("2")).Count);

        using var cleanup = CreateDb();
        await cleanup.Files.Where(f => f.Id == old).ExecuteDeleteAsync();
        Assert.Single(await OutcomesAsync("2"));
    }
}
