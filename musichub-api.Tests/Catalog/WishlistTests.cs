using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Controllers;
using MusicHub.Api.Data;
using Xunit;

namespace MusicHub.Api.Tests.Catalog;

public sealed class WishlistTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public WishlistTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(_connection).Options);

    private static ExternalTrackWishlistRequest External(
        string externalId = "mb-1", string title = "Song", string? disambiguation = null, params string[] artists) =>
        new("musicbrainz", externalId, title, disambiguation,
            (artists.Length == 0 ? ["Band"] : artists).Select(a => new ExternalArtistCreditRequest(a)).ToList());

    private async Task<(int Status, WishlistResponse Body)> WantExternal(ExternalTrackWishlistRequest request)
    {
        using var db = CreateDb();
        var result = await new TracksController(db, new Wishlist(db, TimeProvider.System))
            .WantExternal(request, default);
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        return (objectResult.StatusCode ?? 200, Assert.IsType<WishlistResponse>(objectResult.Value));
    }

    private async Task<(int Status, WishlistResponse Body)> WantCatalog(int id)
    {
        using var db = CreateDb();
        var result = await new TracksController(db, new Wishlist(db, TimeProvider.System)).Want(id, default);
        if (result is NotFoundResult) return (404, null!);
        var ok = Assert.IsType<OkObjectResult>(result);
        return (200, Assert.IsType<WishlistResponse>(ok.Value));
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
    public async Task Catalog_track_is_wanted_once_and_repeating_changes_nothing()
    {
        var id = await Seed("Song", null, "Band");

        var first = await WantCatalog(id);
        var wantedAt = await WantedAt(id);
        var second = await WantCatalog(id);

        Assert.Equal(WishlistStatus.Added, first.Body.Status);
        Assert.True(first.Body.Track!.Wanted);
        Assert.False(first.Body.Track.HasAudio);
        Assert.Equal(WishlistStatus.AlreadyWanted, second.Body.Status);
        Assert.Equal(wantedAt, await WantedAt(id));
    }

    [Fact]
    public async Task Unknown_catalog_track_is_not_found() => Assert.Equal(404, (await WantCatalog(99)).Status);

    [Fact]
    public async Task New_external_track_is_created_wanted_and_repeating_reuses_it()
    {
        var first = await WantExternal(External());
        var second = await WantExternal(External());

        Assert.Equal(201, first.Status);
        Assert.Equal(WishlistStatus.Added, first.Body.Status);
        Assert.True(first.Body.Track!.Wanted);
        Assert.Equal(200, second.Status);
        Assert.Equal(WishlistStatus.AlreadyWanted, second.Body.Status);
        Assert.Equal(first.Body.Track.Id, second.Body.Track!.Id);
        using var db = CreateDb();
        Assert.Equal(1, await db.Tracks.CountAsync());
        Assert.Equal(1, await db.TrackExternalIds.CountAsync());
    }

    [Fact]
    public async Task Known_provider_identifier_reuses_the_catalog_track_even_if_the_text_differs()
    {
        var first = await WantExternal(External(title: "Song"));
        var again = await WantExternal(External(title: "Song (2011 remaster)", artists: "Someone Else"));

        Assert.Equal(first.Body.Track!.Id, again.Body.Track!.Id);
        using var db = CreateDb();
        Assert.Equal(1, await db.Tracks.CountAsync());
    }

    [Fact]
    public async Task Resembling_catalog_track_without_evidence_is_not_merged_and_not_wanted()
    {
        var existing = await Seed("song", null, "BAND");

        var result = await WantExternal(External());

        Assert.Equal(202, result.Status);
        Assert.Equal(WishlistStatus.Unresolved, result.Body.Status);
        Assert.Null(result.Body.Track);
        Assert.Equal([existing], result.Body.CandidateTrackIds);
        using var db = CreateDb();
        Assert.Equal(1, await db.Tracks.CountAsync());
        Assert.Null(await WantedAt(existing));
        Assert.Equal(0, await db.TrackExternalIds.CountAsync());
    }

    [Fact]
    public async Task Repeating_an_unresolved_selection_keeps_one_review_case()
    {
        await Seed("Song", "Live", "Band");

        var first = await WantExternal(External(disambiguation: "studio"));
        var second = await WantExternal(External(disambiguation: "studio"));

        Assert.Equal(first.Body.ReviewId, second.Body.ReviewId);
        using var db = CreateDb();
        Assert.Equal(1, await db.IdentityReviews.CountAsync());
        Assert.Equal(1, await db.Tracks.CountAsync());
    }

    [Fact]
    public async Task Same_title_by_other_artists_is_not_a_candidate()
    {
        await Seed("Song", null, "Other");

        var result = await WantExternal(External());

        Assert.Equal(WishlistStatus.Added, result.Body.Status);
        using var db = CreateDb();
        Assert.Equal(2, await db.Tracks.CountAsync());
    }

    [Fact]
    public async Task Ambiguous_candidates_are_all_reported()
    {
        var a = await Seed("Song", null, "Band");
        var b = await Seed("Song", "Live", "Band", "Guest");

        var result = await WantExternal(External());

        Assert.Equal([a, b], result.Body.CandidateTrackIds);
    }

    [Fact]
    public async Task Incomplete_external_track_is_rejected()
    {
        using var db = CreateDb();
        var result = await new TracksController(db, new Wishlist(db, TimeProvider.System))
            .WantExternal(new ExternalTrackWishlistRequest("musicbrainz", " ", "", null, []), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(bad.Value);
        Assert.Equal(["artists", "externalId", "title"], problem.Errors.Keys.Order());
    }

    [Fact]
    public async Task Too_long_fields_are_rejected_as_bad_request()
    {
        using var db = CreateDb();
        var controller = new TracksController(db, new Wishlist(db, TimeProvider.System));
        var artists = new List<ExternalArtistCreditRequest> { new(new string('a', 501)) };

        var result = await controller.WantExternal(new ExternalTrackWishlistRequest(
            new string('p', 101), new string('e', 201), new string('t', 501), new string('d', 501), artists), default);

        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
        Assert.Equal(["artists", "disambiguation", "externalId", "provider", "title"], problem.Errors.Keys.Order());
    }

    [Fact]
    public async Task Fields_at_the_limit_are_accepted()
    {
        var (status, _) = await WantExternal(new ExternalTrackWishlistRequest(
            new string('p', 100), new string('e', 200), new string('t', 500), new string('d', 500),
            [new(new string('a', 500))]));

        Assert.Equal(201, status);
    }

    [Fact]
    public async Task Null_artist_entry_is_rejected_as_bad_request()
    {
        using var db = CreateDb();
        var result = await new TracksController(db, new Wishlist(db, TimeProvider.System)).WantExternal(
            new ExternalTrackWishlistRequest("musicbrainz", "mb-1", "Song", null, [null!, new("Band")]), default);

        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
        Assert.Equal(["artists"], problem.Errors.Keys);
    }

    [Fact]
    public async Task Created_track_reuses_an_existing_artist_ignoring_case()
    {
        await Seed("Other", null, "Band");

        await WantExternal(External(artists: "  BAND "));

        using var db = CreateDb();
        Assert.Equal(1, await db.Artists.CountAsync());
    }

    private async Task<DateTimeOffset?> WantedAt(int id)
    {
        using var db = CreateDb();
        return (await db.Tracks.AsNoTracking().SingleAsync(t => t.Id == id)).WantedAt;
    }
}
