using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Api.Controllers;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;
using MusicHub.Api.Metadata;

namespace MusicHub.Api.Tests.Ingestion;

public class FileCoverTests
{
    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
    ];

    private static readonly IEmbeddedCoverReader Covers = new TagLibEmbeddedCoverReader();

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

    private static async Task<int> AddAsync(
        MusicHubDbContext db, FakeFileStorage storage, string name, byte[] bytes, FileStatus status = FileStatus.Stored)
    {
        var record = new FileRecord
        {
            OriginalFilename = name,
            StorageLocation = "default",
            StorageKey = $"library/aa/{Guid.NewGuid()}",
            Status = status,
            Source = "upload",
            StartedAt = DateTimeOffset.UtcNow,
            SizeBytes = bytes.Length,
        };
        await storage.PutAsync(record.StorageKey, new MemoryStream(bytes), null, CancellationToken.None);
        db.Files.Add(record);
        await db.SaveChangesAsync();
        return record.Id;
    }

    private static byte[] Mp3WithPicture(byte[] image, string mimeType = "image/png") =>
        AudioSamples.TaggedMp3(tag => tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(image))
        {
            Type = TagLib.PictureType.FrontCover,
            MimeType = mimeType,
        }]);

    [Fact]
    public async Task Cover_WithEmbeddedPicture_ServesItWithTheTypeOfItsBytes()
    {
        var (controller, db, storage) = Create(nameof(Cover_WithEmbeddedPicture_ServesItWithTheTypeOfItsBytes));
        using var _ = db;
        // The tag claims JPEG; the bytes are PNG, and the bytes win.
        var id = await AddAsync(db, storage, "song.mp3", Mp3WithPicture(Png, "image/jpeg"));

        var result = await controller.Cover(id, Covers, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(Png, file.FileContents);
        Assert.Equal("nosniff", controller.Response.Headers.XContentTypeOptions.ToString());
    }

    [Fact]
    public async Task Cover_WithoutPicture_IsNotFound()
    {
        var (controller, db, storage) = Create(nameof(Cover_WithoutPicture_IsNotFound));
        using var _ = db;
        var id = await AddAsync(db, storage, "song.mp3", AudioSamples.TaggedMp3(tag => tag.Title = "No cover"));

        Assert.IsType<NotFoundResult>(await controller.Cover(id, Covers, CancellationToken.None));
    }

    [Fact]
    public async Task Cover_WithPictureThatIsNotAnImage_IsNotFound()
    {
        var (controller, db, storage) = Create(nameof(Cover_WithPictureThatIsNotAnImage_IsNotFound));
        using var _ = db;
        var id = await AddAsync(db, storage, "song.mp3", Mp3WithPicture("<svg onload=alert(1)/>"u8.ToArray(), "image/svg+xml"));

        Assert.IsType<NotFoundResult>(await controller.Cover(id, Covers, CancellationToken.None));
    }

    [Fact]
    public async Task Cover_WithOversizedPicture_IsNotFound()
    {
        var (controller, db, storage) = Create(nameof(Cover_WithOversizedPicture_IsNotFound));
        using var _ = db;
        var big = new byte[TagLibEmbeddedCoverReader.MaxCoverBytes + 1];
        Png.CopyTo(big, 0);
        var id = await AddAsync(db, storage, "song.mp3", Mp3WithPicture(big));

        Assert.IsType<NotFoundResult>(await controller.Cover(id, Covers, CancellationToken.None));
    }

    [Fact]
    public async Task Cover_ForCorruptAudio_IsNotFound()
    {
        var (controller, db, storage) = Create(nameof(Cover_ForCorruptAudio_IsNotFound));
        using var _ = db;
        var id = await AddAsync(db, storage, "song.mp3", [1, 2, 3, 4]);

        Assert.IsType<NotFoundResult>(await controller.Cover(id, Covers, CancellationToken.None));
    }

    [Fact]
    public async Task Cover_ForNonAudioExtension_IsNotFound()
    {
        var (controller, db, storage) = Create(nameof(Cover_ForNonAudioExtension_IsNotFound));
        using var _ = db;
        var id = await AddAsync(db, storage, "notes.txt", Mp3WithPicture(Png));

        Assert.IsType<NotFoundResult>(await controller.Cover(id, Covers, CancellationToken.None));
    }

    [Fact]
    public async Task Cover_ForFileThatIsNotStored_IsNotFound()
    {
        var (controller, db, storage) = Create(nameof(Cover_ForFileThatIsNotStored_IsNotFound));
        using var _ = db;
        var id = await AddAsync(db, storage, "song.mp3", Mp3WithPicture(Png), FileStatus.Uploading);

        Assert.IsType<NotFoundResult>(await controller.Cover(id, Covers, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Cover(id + 100, Covers, CancellationToken.None));
    }
}
