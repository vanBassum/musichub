using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Api.Controllers;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;
using MusicHub.Api.Storage;

namespace MusicHub.Api.Tests.Ingestion;

public class FileAudioTests
{
    private static readonly byte[] Bytes = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();

    private static (FilesController Controller, MusicHubDbContext Db, FakeFileStorage Storage) Create(string dbName)
    {
        var db = new MusicHubDbContext(new DbContextOptionsBuilder<MusicHubDbContext>().UseInMemoryDatabase(dbName).Options);
        var storage = new FakeFileStorage();
        var controller = new FilesController(
            new FileIngestionService(db, storage, new ActiveUploadTracker()),
            new FileDeletionService(db, storage, new ActiveUploadTracker(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<FileDeletionService>.Instance),
            new FileListing(db),
            Options.Create(new FileIngestionOptions()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (controller, db, storage);
    }

    private static async Task<int> AddAsync(MusicHubDbContext db, FakeFileStorage storage, string name, FileStatus status = FileStatus.Stored)
    {
        var record = new FileRecord
        {
            OriginalFilename = name,
            StorageLocation = "default",
            StorageKey = $"library/aa/{Guid.NewGuid()}",
            Status = status,
            Source = "upload",
            StartedAt = DateTimeOffset.UtcNow,
            SizeBytes = Bytes.Length,
        };
        await storage.PutAsync(record.StorageKey, new MemoryStream(Bytes), null, CancellationToken.None);
        db.Files.Add(record);
        await db.SaveChangesAsync();
        return record.Id;
    }

    private static async Task<byte[]> ReadAsync(IActionResult result)
    {
        var stream = Assert.IsType<FileStreamResult>(result).FileStream;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    [Fact]
    public async Task Audio_WithoutRange_ServesWholeFileInlineWithAudioTypeAndAcceptRanges()
    {
        var (controller, db, storage) = Create(nameof(Audio_WithoutRange_ServesWholeFileInlineWithAudioTypeAndAcceptRanges));
        using var _ = db;
        var id = await AddAsync(db, storage, "song.mp3");

        var result = await controller.Audio(id, CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("audio/mpeg", file.ContentType);
        Assert.True(string.IsNullOrEmpty(file.FileDownloadName));
        Assert.Equal("bytes", controller.Response.Headers.AcceptRanges.ToString());
        Assert.Equal(Bytes.Length, controller.Response.ContentLength);
        Assert.Equal(Bytes, await ReadAsync(result));
    }

    [Theory]
    [InlineData("bytes=10-19", 10, 19)]
    [InlineData("bytes=90-", 90, 99)]
    [InlineData("bytes=95-500", 95, 99)]
    [InlineData("bytes=-5", 95, 99)]
    public async Task Audio_WithRange_Returns206WithThatSlice(string header, int start, int end)
    {
        var (controller, db, storage) = Create(nameof(Audio_WithRange_Returns206WithThatSlice) + header);
        using var _ = db;
        var id = await AddAsync(db, storage, "song.flac");
        controller.Request.Headers.Range = header;

        var result = await controller.Audio(id, CancellationToken.None);

        Assert.Equal(StatusCodes.Status206PartialContent, controller.Response.StatusCode);
        Assert.Equal($"bytes {start}-{end}/100", controller.Response.Headers.ContentRange.ToString());
        Assert.Equal(end - start + 1, controller.Response.ContentLength);
        Assert.Equal(Bytes[start..(end + 1)], await ReadAsync(result));
    }

    [Theory]
    [InlineData("bytes=100-")]
    [InlineData("bytes=200-300")]
    [InlineData("bytes=-0")]
    public async Task Audio_WithUnsatisfiableRange_Returns416WithTotalLength(string header)
    {
        var (controller, db, storage) = Create(nameof(Audio_WithUnsatisfiableRange_Returns416WithTotalLength) + header);
        using var _ = db;
        var id = await AddAsync(db, storage, "song.mp3");
        controller.Request.Headers.Range = header;

        var result = await controller.Audio(id, CancellationToken.None);

        Assert.Equal(StatusCodes.Status416RangeNotSatisfiable, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal("bytes */100", controller.Response.Headers.ContentRange.ToString());
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("items=1-2")]
    [InlineData("bytes=0-1,5-6")]
    [InlineData("bytes=20-10")]
    public async Task Audio_WithMalformedOrMultiRange_ServesWholeFile(string header)
    {
        var (controller, db, storage) = Create(nameof(Audio_WithMalformedOrMultiRange_ServesWholeFile) + header);
        using var _ = db;
        var id = await AddAsync(db, storage, "song.mp3");
        controller.Request.Headers.Range = header;

        var result = await controller.Audio(id, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, controller.Response.StatusCode);
        Assert.Equal(Bytes, await ReadAsync(result));
    }

    [Theory]
    [InlineData(FileStatus.Pending)]
    [InlineData(FileStatus.Uploading)]
    [InlineData(FileStatus.Failed)]
    [InlineData(FileStatus.Expired)]
    [InlineData(FileStatus.Deleting)]
    public async Task Audio_WhenNotStored_Returns404(FileStatus status)
    {
        var (controller, db, storage) = Create(nameof(Audio_WhenNotStored_Returns404) + status);
        using var _ = db;
        var id = await AddAsync(db, storage, "song.mp3", status);

        Assert.IsType<NotFoundResult>(await controller.Audio(id, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Audio(id + 1, CancellationToken.None));
    }

    [Fact]
    public async Task Audio_WhenStoredObjectIsMissing_Returns404()
    {
        var (controller, db, _) = Create(nameof(Audio_WhenStoredObjectIsMissing_Returns404));
        using var _ = db;
        var id = await AddAsync(db, new FakeFileStorage(), "song.mp3");

        Assert.IsType<NotFoundResult>(await controller.Audio(id, CancellationToken.None));
    }

    [Fact]
    public async Task Audio_WhenExtensionIsNotAudio_Returns415()
    {
        var (controller, db, storage) = Create(nameof(Audio_WhenExtensionIsNotAudio_Returns415));
        using var _ = db;
        var id = await AddAsync(db, storage, "notes.txt");

        var result = Assert.IsType<ObjectResult>(await controller.Audio(id, CancellationToken.None));

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, result.StatusCode);
    }

    [Fact]
    public async Task Download_StaysAnAttachment()
    {
        var (controller, db, storage) = Create(nameof(Download_StaysAnAttachment));
        using var _ = db;
        var id = await AddAsync(db, storage, "song.mp3");

        var result = Assert.IsType<FileStreamResult>(await controller.Download(id, CancellationToken.None));

        Assert.Equal("application/octet-stream", result.ContentType);
        Assert.Equal("song.mp3", result.FileDownloadName);
    }
}
