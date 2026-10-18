using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;
using Xunit;

namespace MusicHub.Api.Tests.Catalog;

/// <summary>
/// Runs against SQLite (a relational provider) so foreign keys and unique indexes are
/// enforced, which the EF in-memory provider does not do.
/// </summary>
public sealed class CatalogModelTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public CatalogModelTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(_connection).Options);

    private static FileRecord NewFile(string name, int? trackId = null) => new()
    {
        OriginalFilename = name,
        StorageLocation = "default",
        StorageKey = $"library/aa/{Guid.NewGuid()}",
        Source = "upload",
        StartedAt = DateTimeOffset.UtcNow,
        TrackId = trackId,
    };

    [Fact]
    public async Task Track_WithoutFiles_PersistsAndIsQueryable()
    {
        using (var db = CreateDb())
        {
            db.Tracks.Add(new Track { Title = "Unavailable", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        using var read = CreateDb();
        var track = await read.Tracks.Include(t => t.Files).SingleAsync();
        Assert.Empty(track.Files);
        Assert.Single(await read.Tracks.Where(t => t.Files.Count == 0).ToListAsync());
    }

    [Fact]
    public async Task Track_WithMultipleFiles_PersistsAll()
    {
        using (var db = CreateDb())
        {
            var track = new Track { Title = "Song", CreatedAt = DateTimeOffset.UtcNow };
            track.Files.Add(NewFile("song.flac"));
            track.Files.Add(NewFile("song.mp3"));
            db.Tracks.Add(track);
            await db.SaveChangesAsync();
        }

        using var read = CreateDb();
        var loaded = await read.Tracks.Include(t => t.Files).SingleAsync();
        Assert.Equal(2, loaded.Files.Count);
    }

    [Fact]
    public async Task File_WithoutTrack_Persists()
    {
        using (var db = CreateDb())
        {
            db.Files.Add(NewFile("orphan.mp3"));
            await db.SaveChangesAsync();
        }

        using var read = CreateDb();
        Assert.Null((await read.Files.SingleAsync()).TrackId);
    }

    [Fact]
    public async Task File_WithUnknownTrack_IsRejected()
    {
        using var db = CreateDb();
        db.Files.Add(NewFile("bad.mp3", trackId: 999));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DeletingTrack_LeavesItsFilesUnassociated()
    {
        using (var db = CreateDb())
        {
            var track = new Track { Title = "Song", CreatedAt = DateTimeOffset.UtcNow };
            track.Files.Add(NewFile("song.mp3"));
            db.Tracks.Add(track);
            await db.SaveChangesAsync();
            db.Tracks.Remove(track);
            await db.SaveChangesAsync();
        }

        using var read = CreateDb();
        Assert.Null((await read.Files.SingleAsync()).TrackId);
    }

    [Fact]
    public async Task Artist_OnManyTracks_IsNotDuplicated()
    {
        using (var db = CreateDb())
        {
            var artist = new Artist { Name = "Artist", CreatedAt = DateTimeOffset.UtcNow };
            var a = new Track { Title = "A", CreatedAt = DateTimeOffset.UtcNow };
            var b = new Track { Title = "B", CreatedAt = DateTimeOffset.UtcNow };
            a.Artists.Add(new TrackArtist { Artist = artist, Role = "Main" });
            b.Artists.Add(new TrackArtist { Artist = artist, Role = "Featuring" });
            db.Tracks.AddRange(a, b);
            await db.SaveChangesAsync();
        }

        using var read = CreateDb();
        Assert.Equal(1, await read.Artists.CountAsync());
        Assert.Equal(2, await read.Tracks.CountAsync());
        var artistWithTracks = await read.Artists.Include(x => x.Tracks).SingleAsync();
        Assert.Equal(2, artistWithTracks.Tracks.Count);
    }

    [Fact]
    public async Task Track_WithSeveralArtistsAndRoles_Persists()
    {
        using (var db = CreateDb())
        {
            var track = new Track { Title = "Duet", CreatedAt = DateTimeOffset.UtcNow };
            var one = new Artist { Name = "One", CreatedAt = DateTimeOffset.UtcNow };
            var two = new Artist { Name = "Two", CreatedAt = DateTimeOffset.UtcNow };
            track.Artists.Add(new TrackArtist { Artist = one, Role = "Main" });
            track.Artists.Add(new TrackArtist { Artist = two, Role = "Remixer" });
            track.Artists.Add(new TrackArtist { Artist = two, Role = "Main" });
            db.Tracks.Add(track);
            await db.SaveChangesAsync();
        }

        using var read = CreateDb();
        Assert.Equal(3, await read.TrackArtists.CountAsync());
    }

    [Theory]
    [InlineData("Main")]
    [InlineData(null)]
    public async Task TrackArtist_DuplicatePairAndRole_IsRejected(string? role)
    {
        using var db = CreateDb();
        var track = new Track { Title = "Song", CreatedAt = DateTimeOffset.UtcNow };
        var artist = new Artist { Name = "Artist", CreatedAt = DateTimeOffset.UtcNow };
        track.Artists.Add(new TrackArtist { Artist = artist, Role = role });
        db.Tracks.Add(track);
        await db.SaveChangesAsync();

        db.TrackArtists.Add(new TrackArtist { TrackId = track.Id, ArtistId = artist.Id, Role = role });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Artist_WithTracks_CannotBeDeleted()
    {
        using (var db = CreateDb())
        {
            var track = new Track { Title = "Song", CreatedAt = DateTimeOffset.UtcNow };
            track.Artists.Add(new TrackArtist { Artist = new Artist { Name = "Artist", CreatedAt = DateTimeOffset.UtcNow } });
            db.Tracks.Add(track);
            await db.SaveChangesAsync();
        }

        using var del = CreateDb();
        del.Artists.Remove(await del.Artists.SingleAsync());
        await Assert.ThrowsAsync<DbUpdateException>(() => del.SaveChangesAsync());
    }

    [Fact]
    public async Task DeletingTrack_RemovesItsArtistLinksButKeepsArtist()
    {
        using var db = CreateDb();
        var track = new Track { Title = "Song", CreatedAt = DateTimeOffset.UtcNow };
        track.Artists.Add(new TrackArtist { Artist = new Artist { Name = "Artist", CreatedAt = DateTimeOffset.UtcNow } });
        db.Tracks.Add(track);
        await db.SaveChangesAsync();

        db.Tracks.Remove(track);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.TrackArtists.CountAsync());
        Assert.Equal(1, await db.Artists.CountAsync());
    }
}
