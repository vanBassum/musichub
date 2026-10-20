using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Controllers;
using MusicHub.Api.Data;
using Xunit;

namespace MusicHub.Api.Tests.Catalog;

public sealed class LibraryTracksTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public LibraryTracksTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(_connection).Options);

    private static ExternalTrackRequest External(
        string externalId = "mb-1", string title = "Song", string? disambiguation = null, params string[] artists) =>
        new("musicbrainz", externalId, title, disambiguation,
            (artists.Length == 0 ? ["Band"] : artists).Select(a => new ExternalArtistCreditRequest(a)).ToList());

    private async Task<(int Status, AddToLibraryResponse Body)> AddExternal(ExternalTrackRequest request)
    {
        using var db = CreateDb();
        var result = await new TracksController(db, new LibraryTracks(db, TimeProvider.System))
            .AddExternal(request, default);
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        return (objectResult.StatusCode ?? 200, Assert.IsType<AddToLibraryResponse>(objectResult.Value));
    }

    private async Task<int> Seed(string title, string? version, params string[] artists)
    {
        using var db = CreateDb();
        var track = new Track { Title = title, Version = version, CreatedAt = DateTimeOffset.UtcNow };
        foreach (var name in artists)
        {
            var artist = await db.Artists.FirstOrDefaultAsync(a => a.Name == name)
                ?? new Artist { Name = name, CreatedAt = DateTimeOffset.UtcNow };
            track.Artists.Add(new TrackArtist { Artist = artist });
        }
        db.Tracks.Add(track);
        await db.SaveChangesAsync();
        return track.Id;
    }

    [Fact]
    public async Task New_external_track_is_created_as_missing_music_and_repeating_reuses_it()
    {
        var first = await AddExternal(External());
        var second = await AddExternal(External());

        Assert.Equal(201, first.Status);
        Assert.True(first.Body.Created);
        Assert.False(first.Body.Track.HasAudio);
        Assert.Equal(200, second.Status);
        Assert.False(second.Body.Created);
        Assert.Equal(first.Body.Track.Id, second.Body.Track.Id);
        using var db = CreateDb();
        Assert.Equal(1, await db.Tracks.CountAsync());
        Assert.Equal(1, await db.TrackExternalIds.CountAsync());
    }

    [Fact]
    public async Task Known_provider_identifier_reuses_the_catalog_track_even_if_the_text_differs()
    {
        var first = await AddExternal(External(title: "Song"));
        var again = await AddExternal(External(title: "Song (2011 remaster)", artists: "Someone Else"));

        Assert.Equal(first.Body.Track.Id, again.Body.Track.Id);
        using var db = CreateDb();
        Assert.Equal(1, await db.Tracks.CountAsync());
    }

    [Fact]
    public async Task Resembling_catalog_track_is_not_merged_and_a_new_track_is_created()
    {
        var existing = await Seed("song", null, "BAND");

        var result = await AddExternal(External());

        Assert.Equal(201, result.Status);
        Assert.NotEqual(existing, result.Body.Track.Id);
        using var db = CreateDb();
        Assert.Equal(2, await db.Tracks.CountAsync());
        Assert.Equal(1, await db.TrackExternalIds.CountAsync());
    }

    [Fact]
    public async Task Repeating_a_selection_that_resembles_a_catalog_track_stays_one_new_track()
    {
        await Seed("Song", "Live", "Band");

        var first = await AddExternal(External(disambiguation: "studio"));
        var second = await AddExternal(External(disambiguation: "studio"));

        Assert.Equal(first.Body.Track.Id, second.Body.Track.Id);
        Assert.False(second.Body.Created);
        using var db = CreateDb();
        Assert.Equal(2, await db.Tracks.CountAsync());
    }

    [Fact]
    public async Task Incomplete_external_track_is_rejected()
    {
        using var db = CreateDb();
        var result = await new TracksController(db, new LibraryTracks(db, TimeProvider.System))
            .AddExternal(new ExternalTrackRequest("musicbrainz", " ", "", null, []), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(bad.Value);
        Assert.Equal(["artists", "externalId", "title"], problem.Errors.Keys.Order());
    }

    [Fact]
    public async Task Too_long_fields_are_rejected_as_bad_request()
    {
        using var db = CreateDb();
        var controller = new TracksController(db, new LibraryTracks(db, TimeProvider.System));
        var artists = new List<ExternalArtistCreditRequest> { new(new string('a', 501)) };

        var result = await controller.AddExternal(new ExternalTrackRequest(
            new string('p', 101), new string('e', 201), new string('t', 501), new string('d', 501), artists), default);

        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
        Assert.Equal(["artists", "disambiguation", "externalId", "provider", "title"], problem.Errors.Keys.Order());
    }

    [Fact]
    public async Task Fields_at_the_limit_are_accepted()
    {
        var (status, _) = await AddExternal(new ExternalTrackRequest(
            new string('p', 100), new string('e', 200), new string('t', 500), new string('d', 500),
            [new(new string('a', 500))]));

        Assert.Equal(201, status);
    }

    [Fact]
    public async Task Null_artist_entry_is_rejected_as_bad_request()
    {
        using var db = CreateDb();
        var result = await new TracksController(db, new LibraryTracks(db, TimeProvider.System)).AddExternal(
            new ExternalTrackRequest("musicbrainz", "mb-1", "Song", null, [null!, new("Band")]), default);

        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
        Assert.Equal(["artists"], problem.Errors.Keys);
    }

    [Fact]
    public async Task Created_track_reuses_an_existing_artist_ignoring_case()
    {
        await Seed("Other", null, "Band");

        await AddExternal(External(artists: "  BAND "));

        using var db = CreateDb();
        Assert.Equal(1, await db.Artists.CountAsync());
    }
}
