using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;
using MusicHub.Api.Storage;
using Xunit;

namespace MusicHub.Api.Tests.Ingestion;

public class FileIngestionServiceTests
{
    private static MusicHubDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<MusicHubDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new MusicHubDbContext(options);
    }

    [Fact]
    public async Task IngestAsync_WithNonSeekableStream_ComputesCorrectSha256()
    {
        var data = "hello world"u8.ToArray();
        var expectedHash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        using var db = CreateDb(nameof(IngestAsync_WithNonSeekableStream_ComputesCorrectSha256));
        var storage = new FakeFileStorage();
        var service = new FileIngestionService(db, storage, new ActiveUploadTracker());

        var record = await service.IngestAsync(
            new NonSeekableStream(new MemoryStream(data)), "test.txt", null, CancellationToken.None);

        Assert.Equal(expectedHash, record.ContentHash);
    }

    [Fact]
    public async Task IngestAsync_StorageKeyFollowsLibraryLayout()
    {
        var data = new byte[] { 1, 2, 3 };
        using var db = CreateDb(nameof(IngestAsync_StorageKeyFollowsLibraryLayout));
        var storage = new FakeFileStorage();
        var service = new FileIngestionService(db, storage, new ActiveUploadTracker());

        var record = await service.IngestAsync(
            new MemoryStream(data), "song.mp3", null, CancellationToken.None);

        // key must be library/<2chars>/<uuid-with-dashes>
        Assert.NotNull(record.StorageKey);
        var parts = record.StorageKey.Split('/');
        Assert.Equal(3, parts.Length);
        Assert.Equal("library", parts[0]);
        Assert.Equal(2, parts[1].Length);
        Assert.Equal(parts[1], parts[2][..2]);  // two-char prefix matches UUID start
        Assert.True(Guid.TryParse(parts[2], out _), "Third segment must be a valid UUID.");
    }

    [Fact]
    public async Task IngestAsync_SetsRequiredMetadata()
    {
        var data = new byte[] { 42 };
        using var db = CreateDb(nameof(IngestAsync_SetsRequiredMetadata));
        var storage = new FakeFileStorage();
        var service = new FileIngestionService(db, storage, new ActiveUploadTracker());

        var record = await service.IngestAsync(
            new MemoryStream(data), "track.flac", "audio/flac", CancellationToken.None);

        Assert.Equal("track.flac", record.OriginalFilename);
        Assert.Equal("default", record.StorageLocation);
        Assert.Equal("upload", record.Source);
        Assert.NotNull(record.ContentHash);
        Assert.True(record.Id > 0);
    }

    [Fact]
    public async Task IngestAsync_CreatesDbRecordBeforeStorage()
    {
        var data = new byte[] { 1, 2, 3 };
        using var db = CreateDb(nameof(IngestAsync_CreatesDbRecordBeforeStorage));

        int? idAtUploadTime = null;
        var storage = new FakeFileStorage(onPut: key =>
        {
            // At upload time the record must already exist in the database.
            idAtUploadTime = db.Files
                .Where(f => f.StorageKey == key)
                .Select(f => (int?)f.Id)
                .FirstOrDefault();
        });
        var service = new FileIngestionService(db, storage, new ActiveUploadTracker());

        await service.IngestAsync(new MemoryStream(data), "f.mp3", null, CancellationToken.None);

        Assert.NotNull(idAtUploadTime);
        Assert.True(idAtUploadTime > 0);
    }

    [Fact]
    public async Task IngestAsync_OnSuccess_SetsStoredWithHashAndSize()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var db = CreateDb(nameof(IngestAsync_OnSuccess_SetsStoredWithHashAndSize));
        var service = new FileIngestionService(db, new FakeFileStorage(), new ActiveUploadTracker());

        var record = await service.IngestAsync(new MemoryStream(data), "a.mp3", null, CancellationToken.None);

        var saved = db.Files.Single(f => f.Id == record.Id);
        Assert.Equal(FileStatus.Stored, saved.Status);
        Assert.NotNull(saved.ContentHash);
        Assert.Equal(5, saved.SizeBytes);
    }

    [Fact]
    public async Task IngestAsync_WhileStreaming_StatusIsUploadingWithoutHashOrSize()
    {
        using var db = CreateDb(nameof(IngestAsync_WhileStreaming_StatusIsUploadingWithoutHashOrSize));
        FileRecord? seen = null;
        var storage = new FakeFileStorage(onPut: key => seen = db.Files.AsNoTracking().Single(f => f.StorageKey == key));
        var service = new FileIngestionService(db, storage, new ActiveUploadTracker());

        await service.IngestAsync(new MemoryStream([1]), "a.mp3", null, CancellationToken.None);

        Assert.NotNull(seen);
        Assert.Equal(FileStatus.Uploading, seen.Status);
        Assert.Null(seen.ContentHash);
        Assert.Null(seen.SizeBytes);
    }

    [Fact]
    public async Task IngestAsync_WhenStorageFails_PersistsFailedWithoutHashOrSize()
    {
        using var db = CreateDb(nameof(IngestAsync_WhenStorageFails_PersistsFailedWithoutHashOrSize));
        var storage = new FakeFileStorage(onPut: _ => throw new InvalidOperationException("storage down"));
        var service = new FileIngestionService(db, storage, new ActiveUploadTracker());

        var ex = await Assert.ThrowsAsync<FileIngestionException>(() =>
            service.IngestAsync(new MemoryStream([1, 2]), "a.mp3", null, CancellationToken.None));
        Assert.IsType<InvalidOperationException>(ex.InnerException);

        var saved = Assert.Single(db.Files.AsNoTracking());
        Assert.Equal(saved.Id, ex.FileId);
        Assert.Equal(FileStatus.Failed, saved.Status);
        Assert.Null(saved.ContentHash);
        Assert.Null(saved.SizeBytes);
    }

    [Fact]
    public async Task IngestAsync_WhenCancelled_NeverReportsStored()
    {
        using var db = CreateDb(nameof(IngestAsync_WhenCancelled_NeverReportsStored));
        using var cts = new CancellationTokenSource();
        var storage = new FakeFileStorage(onPut: _ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var service = new FileIngestionService(db, storage, new ActiveUploadTracker());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.IngestAsync(new MemoryStream([1, 2]), "a.mp3", null, cts.Token));

        var saved = Assert.Single(db.Files.AsNoTracking());
        Assert.Equal(FileStatus.Uploading, saved.Status);
        Assert.Null(saved.ContentHash);
        Assert.Null(saved.SizeBytes);
    }

    /// <summary>Wraps a stream and makes it non-seekable, to simulate an HTTP request body.</summary>
    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            inner.ReadAsync(buffer, offset, count, ct);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            inner.ReadAsync(buffer, ct);

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
