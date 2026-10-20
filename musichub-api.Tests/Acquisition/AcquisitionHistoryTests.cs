using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Acquisition;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Tests.Acquisition;

/// <summary>Runs on SQLite so the foreign keys are real; a second context stands for a restart.</summary>
public sealed class AcquisitionHistoryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public AcquisitionHistoryTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(_connection).Options);

    private static AcquisitionHistory History(MusicHubDbContext db) => new(db, TimeProvider.System);

    private static AcquisitionCandidate Candidate(string path = @"Music\a\song.mp3", string source = "peer1") =>
        new("slskd", source, path, 1000);

    [Fact]
    public async Task A_rejected_attempt_survives_a_restart_and_blocks_the_same_source()
    {
        await using (var db = CreateDb())
        {
            var history = History(db);
            var download = await history.BeginAsync(Candidate(), null, default);
            await history.DownloadedAsync(download, new DownloadHashes(new string('a', 64),
                new AudioHashResult(AudioHashStatus.Computed, new string('b', 64))), default);
            await history.RejectedAsync(download, "Recording could not be verified.", default);
        }

        await using var restarted = CreateDb();
        var stored = await restarted.AcquisitionDownloads.SingleAsync();
        Assert.Equal(AcquisitionOutcome.Rejected, stored.Outcome);
        Assert.Equal(AcquisitionDownloadState.Downloaded, stored.State);
        Assert.True(await History(restarted).WasRejectedAsync(Candidate(), default));
        Assert.False(await History(restarted).WasRejectedAsync(Candidate(source: "peer2"), default));
        Assert.False(await History(restarted).WasRejectedAsync(Candidate(path: @"Music\a\other.mp3"), default));
        Assert.True(await History(restarted).WasContentRejectedAsync(new string('a', 64), default));
        Assert.Equal(new string('b', 64), stored.AudioHash);
        Assert.Equal(AudioHashStatus.Computed, stored.AudioHashStatus);
        Assert.Equal(Mp3AudioPayload.Version, stored.AudioHashVersion);
        Assert.True(await History(restarted).WasAudioRejectedAsync(new string('b', 64), default));
        Assert.False(await History(restarted).WasAudioRejectedAsync(new string('a', 64), default));
    }

    [Fact]
    public async Task An_unhashable_payload_is_recorded_without_an_audio_hash()
    {
        await using var db = CreateDb();
        var history = History(db);
        var download = await history.BeginAsync(Candidate(), null, default);
        await history.DownloadedAsync(download, new DownloadHashes(new string('a', 64),
            new AudioHashResult(AudioHashStatus.Malformed, null)), default);

        await using var reread = CreateDb();
        var stored = await reread.AcquisitionDownloads.SingleAsync();
        Assert.Equal(new string('a', 64), stored.ContentHash);
        Assert.Null(stored.AudioHash);
        Assert.Equal(AudioHashStatus.Malformed, stored.AudioHashStatus);
    }

    [Fact]
    public async Task Failed_and_accepted_attempts_do_not_block_a_retry()
    {
        await using var db = CreateDb();
        var history = History(db);
        var failed = await history.BeginAsync(Candidate(), null, default);
        await history.FailedAsync(failed, AcquisitionFailure.TimedOut, "Timed out.", default);
        var file = new FileRecord { OriginalFilename = "x.mp3", StorageLocation = "default", StorageKey = "k", Source = "acquisition:slskd" };
        db.Files.Add(file);
        await db.SaveChangesAsync();
        var accepted = await history.BeginAsync(Candidate(source: "peer2"), null, default);
        await history.AcceptedAsync(accepted, file.Id, "Identified.", default);

        Assert.False(await history.WasRejectedAsync(Candidate(), default));
        Assert.False(await history.WasRejectedAsync(Candidate(source: "peer2"), default));
    }

    [Fact]
    public async Task History_stays_when_the_file_is_deleted()
    {
        await using var db = CreateDb();
        var file = new FileRecord { OriginalFilename = "x.mp3", StorageLocation = "default", StorageKey = "k", Source = "acquisition:slskd" };
        db.Files.Add(file);
        await db.SaveChangesAsync();
        var history = History(db);
        var download = await history.BeginAsync(Candidate(), null, default);
        await history.AcceptedAsync(download, file.Id, "Identified.", default);

        db.Files.Remove(file);
        await db.SaveChangesAsync();

        await using var reread = CreateDb();
        var stored = await reread.AcquisitionDownloads.SingleAsync();
        Assert.Equal(AcquisitionOutcome.Accepted, stored.Outcome);
        Assert.Null(stored.FileId);
    }
}
