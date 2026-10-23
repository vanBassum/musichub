using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Controllers;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;
using Xunit;

namespace MusicHub.Api.Tests.Catalog;

/// <summary>Albums are written by seeding the database (the API only reads them), so the tests do the same.</summary>
public sealed class AlbumsTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public AlbumsTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(_connection).Options);

    private static Track NewTrack(string title, string? version, string artist) => new()
    {
        Title = title,
        Version = version,
        CreatedAt = DateTimeOffset.UtcNow,
        Artists = { new TrackArtist { Artist = new Artist { Name = artist, CreatedAt = DateTimeOffset.UtcNow } } },
    };

    private static AlbumEdition NewEdition(string? name, params Track[] tracks)
    {
        var edition = new AlbumEdition { Name = name, CreatedAt = DateTimeOffset.UtcNow };
        for (var i = 0; i < tracks.Length; i++)
            edition.Tracks.Add(new AlbumEditionTrack { Position = i + 1, Track = tracks[i] });
        return edition;
    }

    private async Task<int> Seed(string title, params AlbumEdition[] editions)
    {
        using var db = CreateDb();
        var album = new Album { Title = title, CreatedAt = DateTimeOffset.UtcNow };
        album.Editions.AddRange(editions);
        db.Albums.Add(album);
        await db.SaveChangesAsync();
        return album.Id;
    }

    private async Task<AlbumResponse> Get(int id)
    {
        using var db = CreateDb();
        var result = await new AlbumsController(db).Get(id, default);
        return Assert.IsType<AlbumResponse>(Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task Compilation_KeepsEachTracksOwnArtistInOrder()
    {
        var id = await Seed("Now That's What I Call Music",
            NewEdition(null, NewTrack("Song A", null, "Artist A"), NewTrack("Song B", null, "Artist B"),
                NewTrack("Song C", null, "Artist C")));

        var edition = Assert.Single((await Get(id)).Editions);
        Assert.Equal([1, 2, 3], edition.Tracks.Select(t => t.Position));
        Assert.Equal(["Artist A", "Artist B", "Artist C"],
            edition.Tracks.Select(t => Assert.Single(t.Track.Artists).Name));
    }

    [Fact]
    public async Task TwoEditions_ShareTheTracksTheyHaveInCommon()
    {
        var shared = NewTrack("Shared", null, "Band");
        var bonus = NewTrack("Bonus", null, "Band");
        var id = await Seed("Album",
            NewEdition("Standard", shared),
            NewEdition("Deluxe", shared, bonus));

        var album = await Get(id);
        Assert.Equal(2, album.Editions.Count);
        Assert.Equal(album.Editions[0].Tracks[0].Track.Id, album.Editions[1].Tracks[0].Track.Id);
        Assert.Equal([1, 2], album.Editions.Select(e => e.TrackCount));
        using var db = CreateDb();
        Assert.Equal(2, await db.Tracks.CountAsync());
    }

    [Fact]
    public async Task Remaster_IsADistinctTrackOnItsOwnEdition()
    {
        var original = NewTrack("Song", null, "Band");
        var remaster = NewTrack("Song", "2011 Remaster", "Band");
        var id = await Seed("Album", NewEdition("Original", original), NewEdition("2011 Remaster", remaster));

        var album = await Get(id);
        var originalTrack = album.Editions[0].Tracks[0].Track;
        var remasterTrack = album.Editions[1].Tracks[0].Track;
        Assert.NotEqual(originalTrack.Id, remasterTrack.Id);
        Assert.Equal("2011 Remaster", remasterTrack.Version);
    }

    [Fact]
    public async Task AlbumWithoutFiles_ListsWithZeroAvailable_AndCountsLinkedFiles()
    {
        var first = NewTrack("One", null, "Band");
        var second = NewTrack("Two", null, "Band");
        var id = await Seed("Album", NewEdition(null, first, second));

        var edition = Assert.Single((await Get(id)).Editions);
        Assert.Equal(2, edition.TrackCount);
        Assert.Equal(0, edition.AvailableTrackCount);

        using (var db = CreateDb())
        {
            db.Files.Add(new FileRecord
            {
                OriginalFilename = "one.mp3",
                StorageLocation = "default",
                StorageKey = $"library/aa/{Guid.NewGuid()}",
                Source = "upload",
                StartedAt = DateTimeOffset.UtcNow,
                Status = FileStatus.Stored,
                TrackId = edition.Tracks[0].Track.Id,
            });
            await db.SaveChangesAsync();
        }

        var after = Assert.Single((await Get(id)).Editions);
        Assert.Equal(1, after.AvailableTrackCount);
        Assert.True(after.Tracks[0].Track.HasAudio);
        Assert.False(after.Tracks[1].Track.HasAudio);
    }

    [Fact]
    public async Task List_SearchesByTitle_AndCountsEditions()
    {
        await Seed("Abbey Road", NewEdition("Standard"), NewEdition("Anniversary"));
        await Seed("Revolver", NewEdition(null));

        using var db = CreateDb();
        var result = await new AlbumsController(db).List("ABBEY", 1, 25, default);
        var page = Assert.IsType<PagedResponse<AlbumListItemResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        var item = Assert.Single(page.Items);
        Assert.Equal("Abbey Road", item.Title);
        Assert.Equal(2, item.EditionCount);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task Get_UnknownAlbum_IsNotFound()
    {
        using var db = CreateDb();
        Assert.IsType<NotFoundResult>(await new AlbumsController(db).Get(404, default));
    }

    [Fact]
    public async Task Edition_PositionIsUniquePerEdition_AndExternalIdsAreUniquePerProvider()
    {
        var track = NewTrack("Song", null, "Band");
        using var db = CreateDb();
        var edition = NewEdition(null, track);
        edition.ExternalIds.Add(new AlbumEditionExternalId { Provider = "musicbrainz", ExternalId = "r1" });
        edition.Tracks.Add(new AlbumEditionTrack { Position = 1, Track = NewTrack("Other", null, "Band") });
        db.Albums.Add(new Album { Title = "Album", CreatedAt = DateTimeOffset.UtcNow, Editions = { edition } });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        using var other = CreateDb();
        var first = NewEdition(null);
        first.ExternalIds.Add(new AlbumEditionExternalId { Provider = "musicbrainz", ExternalId = "r1" });
        var second = NewEdition(null);
        second.ExternalIds.Add(new AlbumEditionExternalId { Provider = "musicbrainz", ExternalId = "r1" });
        other.Albums.Add(new Album { Title = "Album", CreatedAt = DateTimeOffset.UtcNow, Editions = { first, second } });
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public async Task Track_OnAnEdition_CannotBeDeleted()
    {
        var track = NewTrack("Song", null, "Band");
        await Seed("Album", NewEdition(null, track));

        using var db = CreateDb();
        db.Tracks.Remove(await db.Tracks.SingleAsync());
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
