using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;
using MusicHub.Api.Storage;

namespace MusicHub.Api.Tests.Ingestion;

/// <summary>Runs on SQLite so the catalog foreign key is real.</summary>
public sealed class FileDeletionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeFileStorage _inner = new();
    private readonly ControllableStorage _storage;
    private readonly ActiveUploadTracker _tracker = new();

    public FileDeletionServiceTests()
    {
        _connection.Open();
        _storage = new ControllableStorage(_inner);
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private sealed class ControllableStorage(FakeFileStorage inner) : IFileStorage
    {
        public bool FailDelete { get; set; }
        public bool IgnoreDelete { get; set; }
        public int DeleteCalls { get; private set; }

        public Task PutAsync(string key, Stream content, string? contentType, CancellationToken ct) =>
            inner.PutAsync(key, content, contentType, ct);

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => inner.OpenReadAsync(key, ct);
        public Task<bool> ExistsAsync(string key, CancellationToken ct) => inner.ExistsAsync(key, ct);

        public Task DeleteAsync(string key, CancellationToken ct)
        {
            DeleteCalls++;
            if (FailDelete)
                throw new IOException("secret-host unreachable");
            return IgnoreDelete ? Task.CompletedTask : inner.DeleteAsync(key, ct);
        }
    }

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(_connection).Options);

    private FileDeletionService CreateService(MusicHubDbContext db) =>
        new(db, _storage, _tracker, TimeProvider.System, NullLogger<FileDeletionService>.Instance);

    private async Task<int> AddAsync(FileStatus status, bool withObject = true, int? trackId = null)
    {
        using var db = CreateDb();
        var record = new FileRecord
        {
            OriginalFilename = "a.mp3",
            StorageLocation = "default",
            StorageKey = $"library/aa/{Guid.NewGuid()}",
            Status = status,
            Source = "upload",
            StartedAt = DateTimeOffset.UtcNow,
            TrackId = trackId,
        };
        db.Files.Add(record);
        await db.SaveChangesAsync();
        if (withObject)
            await _storage.PutAsync(record.StorageKey, new MemoryStream([1, 2, 3]), null, CancellationToken.None);
        return record.Id;
    }

    private async Task<FileRecord> LoadAsync(int id)
    {
        using var db = CreateDb();
        return await db.Files.AsNoTracking().SingleAsync(f => f.Id == id);
    }

    [Theory]
    [InlineData(FileStatus.Stored, true)]
    [InlineData(FileStatus.Failed, true)]
    [InlineData(FileStatus.Failed, false)]
    [InlineData(FileStatus.Expired, false)]
    [InlineData(FileStatus.Pending, false)]
    [InlineData(FileStatus.Uploading, true)]
    public async Task Delete_AnyStatus_RemovesObjectAndLeavesTombstone(FileStatus status, bool withObject)
    {
        var id = await AddAsync(status, withObject);
        using var db = CreateDb();

        var outcome = await CreateService(db).DeleteAsync(id, CancellationToken.None);

        Assert.Equal(FileDeletionOutcome.Deleted, outcome);
        var record = await LoadAsync(id);
        Assert.Equal(FileStatus.Deleted, record.Status);
        Assert.NotNull(record.DeletedAt);
        Assert.Null(record.DeletionError);
        Assert.False(await _storage.ExistsAsync(record.StorageKey, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_MissingObject_Succeeds()
    {
        var id = await AddAsync(FileStatus.Stored, withObject: false);
        using var db = CreateDb();

        Assert.Equal(FileDeletionOutcome.Deleted, await CreateService(db).DeleteAsync(id, CancellationToken.None));
        Assert.Equal(FileStatus.Deleted, (await LoadAsync(id)).Status);
    }

    [Fact]
    public async Task Delete_StorageFailure_StaysDeletingWithSafeDetailAndRetrySucceeds()
    {
        var id = await AddAsync(FileStatus.Stored);
        _storage.FailDelete = true;
        using (var db = CreateDb())
        {
            var ex = await Assert.ThrowsAsync<FileDeletionException>(
                () => CreateService(db).DeleteAsync(id, CancellationToken.None));
            Assert.DoesNotContain("secret-host", ex.Detail);
        }

        var failed = await LoadAsync(id);
        Assert.Equal(FileStatus.Deleting, failed.Status);
        Assert.Contains("IOException", failed.DeletionError);
        Assert.Null(failed.DeletedAt);
        Assert.True(await _storage.ExistsAsync(failed.StorageKey, CancellationToken.None));

        _storage.FailDelete = false;
        using (var db = CreateDb())
            Assert.Equal(FileDeletionOutcome.Deleted, await CreateService(db).DeleteAsync(id, CancellationToken.None));

        var done = await LoadAsync(id);
        Assert.Equal(FileStatus.Deleted, done.Status);
        Assert.Null(done.DeletionError);
        Assert.False(await _storage.ExistsAsync(done.StorageKey, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_ObjectSurvivesTheDelete_IsNotMarkedDeleted()
    {
        var id = await AddAsync(FileStatus.Stored);
        _storage.IgnoreDelete = true;
        using var db = CreateDb();

        await Assert.ThrowsAsync<FileDeletionException>(() => CreateService(db).DeleteAsync(id, CancellationToken.None));

        Assert.Equal(FileStatus.Deleting, (await LoadAsync(id)).Status);
    }

    [Fact]
    public async Task Delete_Twice_IsIdempotentAndKeepsTheTombstone()
    {
        var id = await AddAsync(FileStatus.Stored);
        using var db = CreateDb();
        var service = CreateService(db);

        await service.DeleteAsync(id, CancellationToken.None);
        var first = await LoadAsync(id);
        var calls = _storage.DeleteCalls;
        Assert.Equal(FileDeletionOutcome.Deleted, await service.DeleteAsync(id, CancellationToken.None));

        var second = await LoadAsync(id);
        Assert.Equal(FileStatus.Deleted, second.Status);
        Assert.Equal(first.DeletedAt, second.DeletedAt);
        Assert.Equal(calls, _storage.DeleteCalls);
    }

    [Fact]
    public async Task Delete_ConcurrentDuplicates_AllSucceedAndLeaveOneTombstone()
    {
        var id = await AddAsync(FileStatus.Stored);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var db = CreateDb();
            return await CreateService(db).DeleteAsync(id, CancellationToken.None);
        }));

        Assert.All(outcomes, o => Assert.Equal(FileDeletionOutcome.Deleted, o));
        Assert.Equal(1, _storage.DeleteCalls);
        Assert.Equal(FileStatus.Deleted, (await LoadAsync(id)).Status);
    }

    [Fact]
    public async Task Delete_WhileUploadRunning_IsRefusedAndChangesNothing()
    {
        var id = await AddAsync(FileStatus.Uploading);
        var key = (await LoadAsync(id)).StorageKey;
        using var db = CreateDb();

        using (_tracker.Begin(key))
            Assert.Equal(FileDeletionOutcome.UploadInProgress, await CreateService(db).DeleteAsync(id, CancellationToken.None));

        Assert.Equal(FileStatus.Uploading, (await LoadAsync(id)).Status);
        Assert.True(await _storage.ExistsAsync(key, CancellationToken.None));
        Assert.Equal(0, _storage.DeleteCalls);
    }

    [Fact]
    public async Task Delete_UnknownId_IsNotFound()
    {
        using var db = CreateDb();
        Assert.Equal(FileDeletionOutcome.NotFound, await CreateService(db).DeleteAsync(999, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_KeepsCatalogLinkAndTrack_AndFileIsNeverReacquired()
    {
        int trackId;
        using (var db = CreateDb())
        {
            var track = new Track { Title = "Kept", CreatedAt = DateTimeOffset.UtcNow };
            db.Tracks.Add(track);
            await db.SaveChangesAsync();
            trackId = track.Id;
        }
        var id = await AddAsync(FileStatus.Stored, trackId: trackId);
        using (var db = CreateDb())
            await CreateService(db).DeleteAsync(id, CancellationToken.None);

        using var read = CreateDb();
        var record = await read.Files.AsNoTracking().SingleAsync(f => f.Id == id);
        Assert.Equal(trackId, record.TrackId);
        Assert.Equal("Kept", (await read.Tracks.SingleAsync(t => t.Id == trackId)).Title);
        Assert.True(record.IsDeletionIntended);
    }

    [Fact]
    public async Task Delete_FailedFile_KeepsItsFailureDiagnostics()
    {
        var id = await AddAsync(FileStatus.Failed);
        using (var db = CreateDb())
        {
            var record = await db.Files.SingleAsync(f => f.Id == id);
            record.FailureReason = UploadIntegrityFailure.StorageError;
            record.StatusDetail = "The storage backend failed (X).";
            await db.SaveChangesAsync();
        }
        using (var db = CreateDb())
            await CreateService(db).DeleteAsync(id, CancellationToken.None);

        var done = await LoadAsync(id);
        Assert.Equal(UploadIntegrityFailure.StorageError, done.FailureReason);
        Assert.Equal("The storage backend failed (X).", done.StatusDetail);
    }
}
