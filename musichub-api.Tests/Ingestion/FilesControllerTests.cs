using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MusicHub.Api.Controllers;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;
using MusicHub.Api.Storage;

namespace MusicHub.Api.Tests.Ingestion;

public class FilesControllerTests
{
    private static MusicHubDbContext CreateDb(string dbName) =>
        new(new DbContextOptionsBuilder<MusicHubDbContext>().UseInMemoryDatabase(dbName).Options);

    private static FilesController CreateController(MusicHubDbContext db, IFileStorage storage, byte[]? body = null)
    {
        var controller = new FilesController(
            new FileIngestionService(db, storage), Options.Create(new FileIngestionOptions()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.Request.Body = new MemoryStream(body ?? [1, 2, 3]);
        return controller;
    }

    [Fact]
    public async Task Upload_WhenStored_Returns201WithStoredRecord()
    {
        using var db = CreateDb(nameof(Upload_WhenStored_Returns201WithStoredRecord));
        var controller = CreateController(db, new FakeFileStorage());

        var result = await controller.Upload("a.mp3", null, null, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, objectResult.StatusCode);
        Assert.Equal(FileStatus.Stored, Assert.Single(db.Files.AsNoTracking()).Status);
    }

    [Fact]
    public async Task Upload_WhenStorageFails_ReturnsNon2xxWithFileId()
    {
        using var db = CreateDb(nameof(Upload_WhenStorageFails_ReturnsNon2xxWithFileId));
        var controller = CreateController(db, new FakeFileStorage(_ => throw new InvalidOperationException("storage down")));

        var result = await controller.Upload("a.mp3", null, null, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        var saved = Assert.Single(db.Files.AsNoTracking());
        Assert.Equal(FileStatus.Failed, saved.Status);
        Assert.Equal(saved.Id, problem.Extensions["fileId"]);
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    [Fact]
    public async Task Upload_WhenDeclaredValuesMatch_Returns201()
    {
        using var db = CreateDb(nameof(Upload_WhenDeclaredValuesMatch_Returns201));
        byte[] body = [1, 2, 3];
        var controller = CreateController(db, new FakeFileStorage(), body);

        var result = await controller.Upload("a.mp3", 3, Sha256Hex(body), CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Upload_WhenBodyIsShorterThanDeclared_Returns422AndFailed()
    {
        using var db = CreateDb(nameof(Upload_WhenBodyIsShorterThanDeclared_Returns422AndFailed));
        var controller = CreateController(db, new FakeFileStorage(), [1, 2]);

        var result = await controller.Upload("a.mp3", 3, Sha256Hex([1, 2, 3]), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("Incomplete", problem.Extensions["reason"]);
        Assert.Equal(FileStatus.Failed, Assert.Single(db.Files.AsNoTracking()).Status);
    }

    [Theory]
    [InlineData(null, "abc")]
    [InlineData(3L, null)]
    [InlineData(3L, "not-a-digest")]
    [InlineData(-1L, "0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task Upload_WhenDeclaredValuesAreIncompleteOrMalformed_Returns400AndRegistersNothing(long? length, string? sha)
    {
        using var db = CreateDb(nameof(Upload_WhenDeclaredValuesAreIncompleteOrMalformed_Returns400AndRegistersNothing) + length + sha);
        var controller = CreateController(db, new FakeFileStorage());

        var result = await controller.Upload("a.mp3", length, sha, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Empty(db.Files);
    }

    [Fact]
    public async Task Upload_WhenCancelled_DoesNotReportSuccess()
    {
        using var db = CreateDb(nameof(Upload_WhenCancelled_DoesNotReportSuccess));
        using var cts = new CancellationTokenSource();
        var controller = CreateController(db, new FakeFileStorage(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.Upload("a.mp3", null, null, cts.Token));

        Assert.NotEqual(FileStatus.Stored, Assert.Single(db.Files.AsNoTracking()).Status);
    }

    [Fact]
    public async Task List_ReturnsRegistrationsNewestFirstWithPersistedStatuses()
    {
        using var db = CreateDb(nameof(List_ReturnsRegistrationsNewestFirstWithPersistedStatuses));
        var t = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        db.Files.AddRange(
            NewRecord("old.mp3", t, FileStatus.Stored),
            NewRecord("tie-a.mp3", t.AddMinutes(1), FileStatus.Failed),
            NewRecord("tie-b.mp3", t.AddMinutes(1), FileStatus.Uploading),
            NewRecord("new.mp3", t.AddMinutes(2), FileStatus.Pending));
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FakeFileStorage());

        var result = Assert.IsType<OkObjectResult>(await controller.List(CancellationToken.None));

        var items = Assert.IsAssignableFrom<IEnumerable<FileListItem>>(result.Value).ToList();
        Assert.Equal(["new.mp3", "tie-b.mp3", "tie-a.mp3", "old.mp3"], items.Select(i => i.OriginalFilename));
        Assert.Equal(["Pending", "Uploading", "Failed", "Stored"], items.Select(i => i.Status));
        Assert.Equal(t.AddMinutes(2), items[0].StartedAt);
        Assert.True(items[1].Id > items[2].Id);
    }

    [Fact]
    public void ListItem_ExposesOnlyIdFilenameSizeStatusStartedAt()
    {
        Assert.Equal(
            ["Id", "OriginalFilename", "SizeBytes", "StartedAt", "Status"],
            typeof(FileListItem).GetProperties().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task Upload_WhenBodyExceedsLimit_PropagatesClientErrorInsteadOf502()
    {
        using var db = CreateDb(nameof(Upload_WhenBodyExceedsLimit_PropagatesClientErrorInsteadOf502));
        var controller = CreateController(db, new FakeFileStorage(_ =>
            throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge)));

        var ex = await Assert.ThrowsAsync<BadHttpRequestException>(() =>
            controller.Upload("a.mp3", null, null, CancellationToken.None));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, ex.StatusCode);
        Assert.Equal(FileStatus.Failed, Assert.Single(db.Files.AsNoTracking()).Status);
    }

    private static async Task<int> UploadAsync(FilesController controller, MusicHubDbContext db, string filename)
    {
        await controller.Upload(filename, null, null, CancellationToken.None);
        return db.Files.AsNoTracking().OrderByDescending(f => f.Id).First().Id;
    }

    private static async Task<byte[]> ReadAllAsync(FileStreamResult result)
    {
        using var buffer = new MemoryStream();
        await result.FileStream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public async Task Download_ReturnsExactBytesAsAttachmentNamedAfterOriginalFilename()
    {
        using var db = CreateDb(nameof(Download_ReturnsExactBytesAsAttachmentNamedAfterOriginalFilename));
        var bytes = Enumerable.Range(0, 1000).Select(i => (byte)(i * 7)).ToArray();
        var controller = CreateController(db, new FakeFileStorage(), bytes);
        var id = await UploadAsync(controller, db, "Café – 日本語.mp3");

        var result = Assert.IsType<FileStreamResult>(await controller.Download(id, CancellationToken.None));

        Assert.Equal(bytes, await ReadAllAsync(result));
        Assert.Equal("Café – 日本語.mp3", result.FileDownloadName);
        Assert.Equal("application/octet-stream", result.ContentType);
    }

    [Fact]
    public async Task Download_ContentDispositionIsAttachmentWithEncodedUnicodeName()
    {
        using var db = CreateDb(nameof(Download_ContentDispositionIsAttachmentWithEncodedUnicodeName));
        var controller = CreateController(db, new FakeFileStorage());
        var id = await UploadAsync(controller, db, "Café.mp3");
        var result = Assert.IsType<FileStreamResult>(await controller.Download(id, CancellationToken.None));

        var context = new ActionContext(new DefaultHttpContext(), new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        context.HttpContext.RequestServices = new ServiceCollection()
            .AddLogging().AddControllers().Services.BuildServiceProvider();
        context.HttpContext.Response.Body = new MemoryStream();
        await result.ExecuteResultAsync(context);

        var header = context.HttpContext.Response.Headers.ContentDisposition.ToString();
        Assert.StartsWith("attachment", header);
        Assert.Contains("filename*=UTF-8''Caf%C3%A9.mp3", header);
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\music\\song.mp3", "song.mp3")]
    [InlineData("a/b/c.mp3", "c.mp3")]
    [InlineData("evil\r\nSet-Cookie: x=1.mp3", "evilSet-Cookie: x=1.mp3")]
    [InlineData("..", "download")]
    [InlineData("a/", "download")]
    [InlineData("", "download")]
    public void SafeDownloadName_StripsPathsAndControlCharacters(string original, string expected)
    {
        Assert.Equal(expected, FilesController.SafeDownloadName(original));
    }

    [Fact]
    public async Task Download_WhenRegistrationMissing_Returns404()
    {
        using var db = CreateDb(nameof(Download_WhenRegistrationMissing_Returns404));
        var controller = CreateController(db, new FakeFileStorage());

        Assert.IsType<NotFoundResult>(await controller.Download(42, CancellationToken.None));
    }

    [Theory]
    [InlineData(FileStatus.Pending)]
    [InlineData(FileStatus.Uploading)]
    [InlineData(FileStatus.Failed)]
    public async Task Download_WhenNotStored_Returns404WithoutTouchingStorage(FileStatus status)
    {
        using var db = CreateDb($"{nameof(Download_WhenNotStored_Returns404WithoutTouchingStorage)}-{status}");
        var record = NewRecord("x.mp3", DateTimeOffset.UtcNow, status);
        db.Files.Add(record);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new ThrowingReadStorage(new InvalidOperationException("must not be read")));

        Assert.IsType<NotFoundResult>(await controller.Download(record.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Download_WhenStoredObjectIsMissing_Returns404()
    {
        using var db = CreateDb(nameof(Download_WhenStoredObjectIsMissing_Returns404));
        var record = NewRecord("x.mp3", DateTimeOffset.UtcNow, FileStatus.Stored);
        db.Files.Add(record);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FakeFileStorage());

        Assert.IsType<NotFoundResult>(await controller.Download(record.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Download_WhenStorageFails_Returns502WithoutLeakingDetails()
    {
        using var db = CreateDb(nameof(Download_WhenStorageFails_Returns502WithoutLeakingDetails));
        var record = NewRecord("x.mp3", DateTimeOffset.UtcNow, FileStatus.Stored);
        db.Files.Add(record);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new ThrowingReadStorage(new InvalidOperationException("secret-host:9000 down")));

        var result = Assert.IsType<ObjectResult>(await controller.Download(record.Id, CancellationToken.None));

        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.DoesNotContain("secret-host", problem.Detail);
        Assert.DoesNotContain(record.StorageKey, problem.Detail);
    }

    private sealed class ThrowingReadStorage(Exception error) : IFileStorage
    {
        public Task PutAsync(string key, Stream content, string? contentType, CancellationToken ct) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => throw error;
        public Task<bool> ExistsAsync(string key, CancellationToken ct) => Task.FromResult(false);
        public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;
    }

    private static FileRecord NewRecord(string name, DateTimeOffset startedAt, FileStatus status) => new()
    {
        OriginalFilename = name,
        StorageLocation = "default",
        StorageKey = $"library/xx/{name}",
        Source = "upload",
        StartedAt = startedAt,
        Status = status,
    };

}
