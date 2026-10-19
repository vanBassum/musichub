using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Controllers;
using MusicHub.Api.Data;
using Xunit;

namespace MusicHub.Api.Tests.Catalog;

public sealed class ArtistsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ArtistsControllerTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(_connection).Options);

    private async Task SeedArtist(string name, params string[] trackTitles)
    {
        using var db = CreateDb();
        var artist = new Artist { Name = name, CreatedAt = DateTimeOffset.UtcNow };
        db.Artists.Add(artist);
        foreach (var title in trackTitles)
        {
            var track = new Track { Title = title, CreatedAt = DateTimeOffset.UtcNow };
            track.Artists.Add(new TrackArtist { Artist = artist, Role = "Main" });
            db.Tracks.Add(track);
        }

        await db.SaveChangesAsync();
    }

    private async Task<IActionResult> List(string? search = null, int pageSize = 10)
    {
        using var db = CreateDb();
        return await new ArtistsController(db).List(search, pageSize, default);
    }

    [Fact]
    public async Task List_SearchesNameCaseInsensitively_WithTrackCount()
    {
        await SeedArtist("Echo Band", "One", "Two");
        await SeedArtist("Bob");

        var body = Assert.IsType<ArtistListResponse>(Assert.IsType<OkObjectResult>(await List("ECHO")).Value);

        var artist = Assert.Single(body.Items);
        Assert.Equal("Echo Band", artist.Name);
        Assert.Equal(2, artist.TrackCount);
        Assert.Equal(1, body.Total);
    }

    [Fact]
    public async Task List_LimitsToPageSize_AndReportsTotal()
    {
        await SeedArtist("A");
        await SeedArtist("B");

        var body = Assert.IsType<ArtistListResponse>(Assert.IsType<OkObjectResult>(await List(pageSize: 1)).Value);

        Assert.Equal(["A"], body.Items.Select(a => a.Name));
        Assert.Equal(2, body.Total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ArtistsController.MaxPageSize + 1)]
    public async Task List_WithBadPageSize_ReturnsBadRequest(int pageSize) =>
        Assert.IsType<BadRequestObjectResult>(await List(pageSize: pageSize));

    [Fact]
    public void ListQuery_TranslatesToPostgres()
    {
        using var db = new MusicHubDbContext(
            new DbContextOptionsBuilder<MusicHubDbContext>().UseNpgsql("Host=localhost").Options);

        var filtered = ArtistListQuery.Filter(db.Artists.AsNoTracking(), "a");

        Assert.NotEmpty(filtered.ToQueryString());
        Assert.NotEmpty(ArtistListQuery.Page(filtered, 10).ToQueryString());
    }
}
