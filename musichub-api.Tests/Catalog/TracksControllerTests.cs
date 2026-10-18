using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Controllers;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;
using Xunit;

namespace MusicHub.Api.Tests.Catalog;

public sealed class TracksControllerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public TracksControllerTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(_connection).Options);

    private static T Body<T>(IActionResult result) => Assert.IsType<T>(Assert.IsAssignableFrom<ObjectResult>(result).Value);

    private static ValidationProblemDetails Invalid(IActionResult result)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        return Assert.IsType<ValidationProblemDetails>(bad.Value);
    }

    private async Task<TrackResponse> Create(string title, string? version = null, params TrackArtistRequest[] artists)
    {
        using var db = CreateDb();
        var result = await new TracksController(db).Create(new TrackRequest(title, version, [.. artists]), default);
        return Body<TrackResponse>(result);
    }

    private async Task AddStoredFile(int trackId, FileStatus status = FileStatus.Stored)
    {
        using var db = CreateDb();
        db.Files.Add(new FileRecord
        {
            OriginalFilename = "a.mp3",
            StorageLocation = "default",
            StorageKey = $"library/aa/{Guid.NewGuid()}",
            Source = "upload",
            StartedAt = DateTimeOffset.UtcNow,
            Status = status,
            TrackId = trackId,
        });
        await db.SaveChangesAsync();
    }

    private async Task<PagedResponse<TrackResponse>> List(
        string? search = null, bool? hasAudio = null, int page = 1, int pageSize = 25)
    {
        using var db = CreateDb();
        return Body<PagedResponse<TrackResponse>>(
            await new TracksController(db).List(search, hasAudio, page, pageSize, default));
    }

    [Fact]
    public async Task Create_WithoutFile_AppearsInDetailAndList()
    {
        using var db = CreateDb();
        var result = await new TracksController(db).Create(new TrackRequest("  Song  ", " Radio edit ", null), default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var track = Assert.IsType<TrackResponse>(created.Value);
        Assert.Equal("Song", track.Title);
        Assert.Equal("Radio edit", track.Version);
        Assert.False(track.HasAudio);

        var detail = Body<TrackResponse>(await new TracksController(db).Get(track.Id, default));
        Assert.Equal(track.Id, detail.Id);
        Assert.Equal(track.Id, Assert.Single((await List()).Items).Id);
    }

    [Fact]
    public async Task Create_WithArtists_ReusesExistingByNameAndId()
    {
        var first = await Create("A", null, new TrackArtistRequest(null, "Artist", "Main"));
        var artistId = first.Artists.Single().ArtistId;

        var second = await Create("B", null,
            new TrackArtistRequest(null, "ARTIST", "Main"),
            new TrackArtistRequest(artistId, null, "Remixer"),
            new TrackArtistRequest(null, "Other", null));

        Assert.Equal(3, second.Artists.Count);
        Assert.Equal(2, second.Artists.Count(a => a.ArtistId == artistId));
        using var db = CreateDb();
        Assert.Equal(2, await db.Artists.CountAsync());
    }

    [Fact]
    public async Task Create_WithNewArtistNameTwice_CreatesOneArtist()
    {
        var track = await Create("A", null,
            new TrackArtistRequest(null, "New", "Main"),
            new TrackArtistRequest(null, "new", "Featuring"));

        Assert.Equal(2, track.Artists.Count);
        using var db = CreateDb();
        Assert.Equal(1, await db.Artists.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutTitle_IsRejected(string? title)
    {
        using var db = CreateDb();
        var problem = Invalid(await new TracksController(db).Create(new TrackRequest(title, null, null), default));
        Assert.Contains("title", problem.Errors.Keys);
        Assert.Equal(0, await db.Tracks.CountAsync());
    }

    [Fact]
    public async Task Create_WithTooLongTitle_IsRejected()
    {
        using var db = CreateDb();
        var title = new string('x', TracksController.MaxTitleLength + 1);
        var problem = Invalid(await new TracksController(db).Create(new TrackRequest(title, null, null), default));
        Assert.Contains("title", problem.Errors.Keys);
    }

    [Fact]
    public async Task Create_WithInvalidArtists_IsRejectedAndSavesNothing()
    {
        using var db = CreateDb();
        var request = new TrackRequest("Song", null,
        [
            new TrackArtistRequest(null, null, null),
            new TrackArtistRequest(1, "Both", null),
            new TrackArtistRequest(999, null, null),
            new TrackArtistRequest(null, "Same", "Main"),
            new TrackArtistRequest(null, "same", "Main"),
            new TrackArtistRequest(null, "Long role", new string('r', TracksController.MaxRoleLength + 1)),
        ]);

        var problem = Invalid(await new TracksController(db).Create(request, default));

        Assert.Contains("artists[0]", problem.Errors.Keys);
        Assert.Contains("artists[1]", problem.Errors.Keys);
        Assert.Contains("artists[5].role", problem.Errors.Keys);
        Assert.Equal(0, await db.Tracks.CountAsync());
        Assert.Equal(0, await db.Artists.CountAsync());
    }

    [Fact]
    public async Task Create_WithUnknownArtistId_IsRejected()
    {
        using var db = CreateDb();
        var request = new TrackRequest("Song", null, [new TrackArtistRequest(999, null, null)]);
        var problem = Invalid(await new TracksController(db).Create(request, default));
        Assert.Contains("artists[0].artistId", problem.Errors.Keys);
    }

    [Fact]
    public async Task Create_WithDuplicateArtistAndRole_IsRejected()
    {
        using var db = CreateDb();
        var request = new TrackRequest("Song", null,
        [
            new TrackArtistRequest(null, "Same", "Main"),
            new TrackArtistRequest(null, "same", " Main "),
        ]);
        var problem = Invalid(await new TracksController(db).Create(request, default));
        Assert.Contains("artists[1]", problem.Errors.Keys);
    }

    [Fact]
    public async Task Get_Unknown_IsNotFound()
    {
        using var db = CreateDb();
        Assert.IsType<NotFoundResult>(await new TracksController(db).Get(42, default));
    }

    [Fact]
    public async Task List_IsPaginatedInTitleOrder()
    {
        foreach (var title in new[] { "C", "A", "E", "B", "D" })
            await Create(title);

        var first = await List(page: 1, pageSize: 2);
        var last = await List(page: 3, pageSize: 2);

        Assert.Equal(5, first.Total);
        Assert.Equal(["A", "B"], first.Items.Select(t => t.Title));
        Assert.Equal(["E"], last.Items.Select(t => t.Title));
        Assert.Equal(3, last.Page);
        Assert.Equal(2, last.PageSize);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 0)]
    [InlineData(1, TracksController.MaxPageSize + 1)]
    public async Task List_WithInvalidPaging_IsRejected(int page, int pageSize)
    {
        using var db = CreateDb();
        Invalid(await new TracksController(db).List(null, null, page, pageSize, default));
    }

    [Fact]
    public async Task List_SearchMatchesTitleVersionAndArtistIgnoringCase()
    {
        await Create("Blue Monday");
        await Create("Other", "Live at Wembley");
        await Create("Third", null, new TrackArtistRequest(null, "New Order", "Main"));

        Assert.Equal("Blue Monday", Assert.Single((await List("MONDAY")).Items).Title);
        Assert.Equal("Other", Assert.Single((await List("wembley")).Items).Title);
        var byArtist = await List("new order");
        Assert.Equal("Third", Assert.Single(byArtist.Items).Title);
        Assert.Equal(1, byArtist.Total);
        Assert.Empty((await List("nothing")).Items);
    }

    [Fact]
    public async Task List_ReportsAudioFromStoredFilesOnly()
    {
        var withAudio = await Create("With");
        var failedOnly = await Create("Failed");
        var without = await Create("Without");
        await AddStoredFile(withAudio.Id);
        await AddStoredFile(failedOnly.Id, FileStatus.Failed);

        var all = (await List()).Items.ToDictionary(t => t.Title, t => t.HasAudio);
        Assert.True(all["With"]);
        Assert.False(all["Failed"]);
        Assert.False(all["Without"]);

        Assert.Equal(["With"], (await List(hasAudio: true)).Items.Select(t => t.Title));
        Assert.Equal(["Failed", "Without"], (await List(hasAudio: false)).Items.Select(t => t.Title));
        Assert.True(Body<TrackResponse>(await GetDetail(withAudio.Id)).HasAudio);
        _ = without;
    }

    private async Task<IActionResult> GetDetail(int id)
    {
        using var db = CreateDb();
        return await new TracksController(db).Get(id, default);
    }

    [Fact]
    public async Task Update_ReplacesMetadataAndArtists()
    {
        var track = await Create("Old", "v1",
            new TrackArtistRequest(null, "Keep", "Main"),
            new TrackArtistRequest(null, "Drop", "Main"));
        var keepId = track.Artists.Single(a => a.Name == "Keep").ArtistId;

        using var db = CreateDb();
        var result = await new TracksController(db).Update(track.Id, new TrackRequest(
            "New", null,
            [new TrackArtistRequest(keepId, null, "Main"), new TrackArtistRequest(null, "Added", "Featuring")]), default);

        var updated = Body<TrackResponse>(result);
        Assert.Equal("New", updated.Title);
        Assert.Null(updated.Version);
        Assert.Equal(["Keep", "Added"], updated.Artists.Select(a => a.Name));
        Assert.Equal(2, await db.TrackArtists.CountAsync());
        Assert.Equal(3, await db.Artists.CountAsync()); // artists are never deleted by editing a track
    }

    [Fact]
    public async Task Update_WithInvalidBody_IsRejectedAndChangesNothing()
    {
        var track = await Create("Song");

        using var db = CreateDb();
        Invalid(await new TracksController(db).Update(track.Id, new TrackRequest(" ", null, null), default));

        Assert.Equal("Song", (await db.Tracks.SingleAsync()).Title);
    }

    [Fact]
    public async Task Update_Unknown_IsNotFound()
    {
        using var db = CreateDb();
        Assert.IsType<NotFoundResult>(
            await new TracksController(db).Update(42, new TrackRequest("Song", null, null), default));
    }
}
