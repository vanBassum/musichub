using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;
using MusicHub.Api.Storage;

namespace MusicHub.Api.Tests.Ingestion;

public class StaleUploadCleanupTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Current;
    }

    private sealed class FlakyStorage(FakeFileStorage inner) : IFileStorage
    {
        public bool FailExists { get; set; }

        public Task PutAsync(string key, Stream content, string? contentType, CancellationToken ct) =>
            inner.PutAsync(key, content, contentType, ct);

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => inner.OpenReadAsync(key, ct);

        public Task<bool> ExistsAsync(string key, CancellationToken ct) =>
            FailExists ? throw new IOException("storage unreachable") : inner.ExistsAsync(key, ct);

        public Task DeleteAsync(string key, CancellationToken ct) => inner.DeleteAsync(key, ct);
    }

    private static MusicHubDbContext CreateDb(string dbName) =>
        new(new DbContextOptionsBuilder<MusicHubDbContext>().UseInMemoryDatabase(dbName).Options);

    private static StaleUploadCleanup CreateCleanup(
        MusicHubDbContext db, IFileStorage storage, ActiveUploadTracker? tracker = null, TimeProvider? time = null) =>
        new(db, storage, tracker ?? new ActiveUploadTracker(), Options.Create(new FileIngestionOptions()),
            time ?? new FixedTime(Now), NullLogger<StaleUploadCleanup>.Instance);

    private static async Task<FileRecord> AddAsync(
        MusicHubDbContext db, FileStatus status, TimeSpan age, string key = "library/aa/aaaa")
    {
        var record = new FileRecord
        {
            OriginalFilename = "a.mp3",
            StorageLocation = "default",
            StorageKey = key,
            Status = status,
            Source = "upload",
            StartedAt = Now - age,
        };
        db.Files.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    private static Task PutAsync(IFileStorage storage, string key) =>
        storage.PutAsync(key, new MemoryStream([1, 2, 3]), null, CancellationToken.None);

    [Theory]
    [InlineData(FileStatus.Pending)]
    [InlineData(FileStatus.Uploading)]
    public async Task Run_StaleUnfinishedWithoutObject_BecomesExpiredWithDetail(FileStatus status)
    {
        using var db = CreateDb($"{nameof(Run_StaleUnfinishedWithoutObject_BecomesExpiredWithDetail)}-{status}");
        await AddAsync(db, status, TimeSpan.FromHours(2));

        var expired = await CreateCleanup(db, new FakeFileStorage()).RunAsync(CancellationToken.None);

        Assert.Equal(1, expired);
        var stored = await db.Files.AsNoTracking().SingleAsync();
        Assert.Equal(FileStatus.Expired, stored.Status);
        Assert.Equal(StaleUploadCleanup.ExpiredDetail, stored.StatusDetail);
    }

    [Fact]
    public async Task Run_RecentUnfinished_IsLeftAlone()
    {
        using var db = CreateDb(nameof(Run_RecentUnfinished_IsLeftAlone));
        await AddAsync(db, FileStatus.Uploading, TimeSpan.FromMinutes(59));

        Assert.Equal(0, await CreateCleanup(db, new FakeFileStorage()).RunAsync(CancellationToken.None));
        Assert.Equal(FileStatus.Uploading, (await db.Files.AsNoTracking().SingleAsync()).Status);
    }

    [Theory]
    [InlineData(FileStatus.Stored)]
    [InlineData(FileStatus.Failed)]
    [InlineData(FileStatus.Expired)]
    public async Task Run_TerminalRecords_AreNeverChanged(FileStatus status)
    {
        using var db = CreateDb($"{nameof(Run_TerminalRecords_AreNeverChanged)}-{status}");
        await AddAsync(db, status, TimeSpan.FromDays(3));

        Assert.Equal(0, await CreateCleanup(db, new FakeFileStorage()).RunAsync(CancellationToken.None));
        Assert.Equal(status, (await db.Files.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Run_ObjectPresent_IsNeitherStoredNorDeleted()
    {
        using var db = CreateDb(nameof(Run_ObjectPresent_IsNeitherStoredNorDeleted));
        var storage = new FakeFileStorage();
        var record = await AddAsync(db, FileStatus.Uploading, TimeSpan.FromHours(2));
        await PutAsync(storage, record.StorageKey);

        Assert.Equal(0, await CreateCleanup(db, storage).RunAsync(CancellationToken.None));

        var stored = await db.Files.AsNoTracking().SingleAsync();
        Assert.Equal(FileStatus.Uploading, stored.Status);
        Assert.Null(stored.ContentHash);
        Assert.True(await storage.ExistsAsync(record.StorageKey, CancellationToken.None));
    }

    [Fact]
    public async Task Run_ActiveUploadOlderThanThreshold_IsNotExpired()
    {
        using var db = CreateDb(nameof(Run_ActiveUploadOlderThanThreshold_IsNotExpired));
        var tracker = new ActiveUploadTracker();
        var record = await AddAsync(db, FileStatus.Uploading, TimeSpan.FromHours(5));

        using (tracker.Begin(record.StorageKey))
            Assert.Equal(0, await CreateCleanup(db, new FakeFileStorage(), tracker).RunAsync(CancellationToken.None));
        Assert.Equal(FileStatus.Uploading, (await db.Files.AsNoTracking().SingleAsync()).Status);

        // Once the upload is gone (finished or the process restarted) the record is resolved.
        Assert.Equal(1, await CreateCleanup(db, new FakeFileStorage(), tracker).RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Run_StorageUnreachable_LeavesRecordAndRetriesLater()
    {
        using var db = CreateDb(nameof(Run_StorageUnreachable_LeavesRecordAndRetriesLater));
        var storage = new FlakyStorage(new FakeFileStorage()) { FailExists = true };
        await AddAsync(db, FileStatus.Pending, TimeSpan.FromHours(2));
        var cleanup = CreateCleanup(db, storage);

        Assert.Equal(0, await cleanup.RunAsync(CancellationToken.None));
        Assert.Equal(FileStatus.Pending, (await db.Files.AsNoTracking().SingleAsync()).Status);

        storage.FailExists = false;
        Assert.Equal(1, await cleanup.RunAsync(CancellationToken.None));
        Assert.Equal(FileStatus.Expired, (await db.Files.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Run_Twice_IsIdempotent()
    {
        using var db = CreateDb(nameof(Run_Twice_IsIdempotent));
        await AddAsync(db, FileStatus.Uploading, TimeSpan.FromHours(2));
        var cleanup = CreateCleanup(db, new FakeFileStorage());

        Assert.Equal(1, await cleanup.RunAsync(CancellationToken.None));
        Assert.Equal(0, await cleanup.RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Run_AfterRestart_ExpiresUploadsTheDeadProcessLeftBehind()
    {
        const string dbName = nameof(Run_AfterRestart_ExpiresUploadsTheDeadProcessLeftBehind);
        using (var before = CreateDb(dbName))
            await AddAsync(before, FileStatus.Uploading, TimeSpan.FromHours(2));

        // A fresh process: new context, empty tracker.
        using var after = CreateDb(dbName);
        Assert.Equal(1, await CreateCleanup(after, new FakeFileStorage(), new ActiveUploadTracker()).RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Run_WhileRealUploadIsStillRunning_DoesNotRaceIt()
    {
        const string dbName = nameof(Run_WhileRealUploadIsStillRunning_DoesNotRaceIt);
        var tracker = new ActiveUploadTracker();
        var time = new FixedTime(DateTimeOffset.UtcNow);
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var storage = new BlockingStorage(started, release.Task);

        using var uploadDb = CreateDb(dbName);
        var upload = new FileIngestionService(uploadDb, storage, tracker)
            .IngestAsync(new MemoryStream([1, 2, 3]), "a.mp3", null, CancellationToken.None);
        await started.Task;

        // Hours later the upload is still running: the sweep must not touch it.
        time.Current = DateTimeOffset.UtcNow.AddHours(3);
        using (var sweepDb = CreateDb(dbName))
            Assert.Equal(0, await CreateCleanup(sweepDb, storage, tracker, time).RunAsync(CancellationToken.None));

        release.SetResult();
        Assert.Equal(FileStatus.Stored, (await upload).Status);

        using var checkDb = CreateDb(dbName);
        Assert.Equal(0, await CreateCleanup(checkDb, storage, tracker, time).RunAsync(CancellationToken.None));
        Assert.Equal(FileStatus.Stored, (await checkDb.Files.AsNoTracking().SingleAsync()).Status);
    }

    private sealed class BlockingStorage(TaskCompletionSource started, Task release) : IFileStorage
    {
        private readonly FakeFileStorage _inner = new();

        public async Task PutAsync(string key, Stream content, string? contentType, CancellationToken ct)
        {
            started.SetResult();
            await release;
            await _inner.PutAsync(key, content, contentType, ct);
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => _inner.OpenReadAsync(key, ct);
        public Task<bool> ExistsAsync(string key, CancellationToken ct) => _inner.ExistsAsync(key, ct);
        public Task DeleteAsync(string key, CancellationToken ct) => _inner.DeleteAsync(key, ct);
    }
}
