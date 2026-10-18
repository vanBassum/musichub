using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        var result = await controller.Upload("a.mp3", CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, objectResult.StatusCode);
        Assert.Equal(FileStatus.Stored, Assert.Single(db.Files.AsNoTracking()).Status);
    }

    [Fact]
    public async Task Upload_WhenStorageFails_ReturnsNon2xxWithFileId()
    {
        using var db = CreateDb(nameof(Upload_WhenStorageFails_ReturnsNon2xxWithFileId));
        var controller = CreateController(db, new FakeFileStorage(_ => throw new InvalidOperationException("storage down")));

        var result = await controller.Upload("a.mp3", CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        var saved = Assert.Single(db.Files.AsNoTracking());
        Assert.Equal(FileStatus.Failed, saved.Status);
        Assert.Equal(saved.Id, problem.Extensions["fileId"]);
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
            controller.Upload("a.mp3", cts.Token));

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
            controller.Upload("a.mp3", CancellationToken.None));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, ex.StatusCode);
        Assert.Equal(FileStatus.Failed, Assert.Single(db.Files.AsNoTracking()).Status);
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
