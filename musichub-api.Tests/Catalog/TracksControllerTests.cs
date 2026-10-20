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

    /// <summary>Puts a track straight into the database; the API has no way to write one.</summary>
    private async Task<int> Seed(string title, string? version = null, params (string Name, string? Role)[] artists)
    {
        using var db = CreateDb();
        var track = new Track { Title = title, Version = version, CreatedAt = DateTimeOffset.UtcNow };
        foreach (var (name, role) in artists)
        {
            var artist = await db.Artists.FirstOrDefaultAsync(a => a.Name == name)
                ?? new Artist { Name = name, CreatedAt = DateTimeOffset.UtcNow };
            track.Artists.Add(new TrackArtist { Artist = artist, Role = role });
        }

        db.Tracks.Add(track);
        await db.SaveChangesAsync();
        return track.Id;
    }

    private async Task AddFile(int trackId, FileStatus status = FileStatus.Stored)
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
        string? search = null, bool? hasAudio = null, int page = 1, int pageSize = 25, string? fields = null)
    {
        using var db = CreateDb();
        return Body<PagedResponse<TrackResponse>>(
            await new TracksController(db, new LibraryTracks(db, TimeProvider.System)).List(search, hasAudio, fields, page, pageSize, default));
    }

    private async Task<IActionResult> GetDetail(int id)
    {
        using var db = CreateDb();
        return await new TracksController(db, new LibraryTracks(db, TimeProvider.System)).Get(id, default);
    }

    [Fact]
    public async Task Get_ReturnsTrackWithArtistsAndNoAudio()
    {
        var id = await Seed("Song", "Radio edit", ("Artist", "Main"), ("Other", null));

        var track = Body<TrackResponse>(await GetDetail(id));

        Assert.Equal("Song", track.Title);
        Assert.Equal("Radio edit", track.Version);
        Assert.False(track.HasAudio);
        Assert.Equal(["Artist", "Other"], track.Artists.Select(a => a.Name));
        Assert.Equal(["Main", null], track.Artists.Select(a => a.Role));
        Assert.Equal(id, Assert.Single((await List()).Items).Id);
    }

    [Fact]
    public async Task Get_Unknown_IsNotFound()
    {
        Assert.IsType<NotFoundResult>(await GetDetail(42));
    }

    [Fact]
    public async Task List_IsPaginatedInTitleOrder()
    {
        foreach (var title in new[] { "C", "A", "E", "B", "D" })
            await Seed(title);

        var first = await List(page: 1, pageSize: 2);
        var last = await List(page: 3, pageSize: 2);

        Assert.Equal(5, first.Total);
        Assert.Equal(["A", "B"], first.Items.Select(t => t.Title));
        Assert.Equal(["E"], last.Items.Select(t => t.Title));
        Assert.Equal(3, last.Page);
        Assert.Equal(2, last.PageSize);
    }

    [Fact]
    public async Task List_WithHugePage_ReturnsEmptyPage()
    {
        await Seed("A");

        var result = await List(page: int.MaxValue, pageSize: TracksController.MaxPageSize);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.Total);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 0)]
    [InlineData(1, TracksController.MaxPageSize + 1)]
    public async Task List_WithInvalidPaging_IsRejected(int page, int pageSize)
    {
        using var db = CreateDb();
        Invalid(await new TracksController(db, new LibraryTracks(db, TimeProvider.System)).List(null, null, null, page, pageSize, default));
    }

    [Fact]
    public async Task List_SearchMatchesTitleVersionAndArtistIgnoringCase()
    {
        await Seed("Blue Monday");
        await Seed("Other", "Live at Wembley");
        await Seed("Third", null, ("New Order", "Main"));

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
        var withAudio = await Seed("With");
        var failedOnly = await Seed("Failed");
        await Seed("Without");
        await AddFile(withAudio);
        await AddFile(failedOnly, FileStatus.Failed);

        var all = (await List()).Items.ToDictionary(t => t.Title, t => t.HasAudio);
        Assert.True(all["With"]);
        Assert.False(all["Failed"]);
        Assert.False(all["Without"]);

        Assert.Equal(["With"], (await List(hasAudio: true)).Items.Select(t => t.Title));
        Assert.Equal(["Failed", "Without"], (await List(hasAudio: false)).Items.Select(t => t.Title));
        Assert.True(Body<TrackResponse>(await GetDetail(withAudio)).HasAudio);
    }

    [Fact]
    public async Task AudioFileId_is_the_stored_file_linked_to_that_track_only()
    {
        var withAudio = await Seed("Same title");
        var other = await Seed("Same title");
        var failedOnly = await Seed("Failed");
        await AddFile(withAudio, FileStatus.Failed);
        await AddFile(withAudio);
        await AddFile(failedOnly, FileStatus.Failed);

        using var db = CreateDb();
        var storedId = db.Files.Single(f => f.TrackId == withAudio && f.Status == FileStatus.Stored).Id;

        Assert.Equal(storedId, Body<TrackResponse>(await GetDetail(withAudio)).AudioFileId);
        Assert.Null(Body<TrackResponse>(await GetDetail(other)).AudioFileId);
        Assert.Null(Body<TrackResponse>(await GetDetail(failedOnly)).AudioFileId);
    }

    [Fact]
    public async Task Track_is_missing_music_until_a_stored_file_is_linked_and_again_after_the_last_is_removed()
    {
        var id = await Seed("Song");
        Assert.Equal(["Song"], (await List(hasAudio: false)).Items.Select(t => t.Title));

        await AddFile(id);
        Assert.Empty((await List(hasAudio: false)).Items);

        using (var db = CreateDb())
        {
            db.Files.RemoveRange(db.Files.Where(f => f.TrackId == id));
            await db.SaveChangesAsync();
        }
        Assert.Equal(["Song"], (await List(hasAudio: false)).Items.Select(t => t.Title));
    }

    [Fact]
    public async Task List_OnEmptyCatalog_ReturnsEmptyPage()
    {
        var page = await List();
        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task List_WithFields_SearchesOnlyThoseFields()
    {
        await Seed("Echo", null, ("Bob", "Main"));
        await Seed("Other", null, ("Echo Band", "Main"));

        Assert.Equal(["Echo"], (await List("echo", fields: "title")).Items.Select(t => t.Title));
        Assert.Equal(["Other"], (await List("echo", fields: "artist")).Items.Select(t => t.Title));
        Assert.Equal(2, (await List("echo", fields: "title,artist")).Total);
        Assert.Equal(2, (await List("echo")).Total);
    }

    [Theory]
    [InlineData("album")]
    [InlineData("title,album")]
    [InlineData(",")]
    public async Task List_WithUnknownOrEmptyFields_ReturnsBadRequest(string fields)
    {
        using var db = CreateDb();
        var result = await new TracksController(db, new LibraryTracks(db, TimeProvider.System)).List("a", null, fields, 1, 25, default);
        Assert.Contains("fields", Invalid(result).Errors.Keys);
    }

    [Fact]
    public void ListQueries_TranslateToPostgres()
    {
        // The other tests run on SQLite; this proves the queries also translate for the production provider.
        using var db = new MusicHubDbContext(
            new DbContextOptionsBuilder<MusicHubDbContext>().UseNpgsql("Host=localhost").Options);

        var filtered = TrackListQuery.Filter(db.Tracks.AsNoTracking(), "a", hasAudio: true, fields: (true, false));

        Assert.NotEmpty(filtered.ToQueryString());
        Assert.NotEmpty(TrackListQuery.Page(filtered, 1, 25).ToQueryString());
    }
}
