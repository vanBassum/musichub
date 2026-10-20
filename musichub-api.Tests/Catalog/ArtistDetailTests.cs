using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Controllers;
using MusicHub.Api.Data;
using MusicHub.Api.ExternalSearch;

namespace MusicHub.Api.Tests.Catalog;

public sealed class ArtistDetailTests : IDisposable
{
    private const string Mbid = "5b11f4ce-a62d-471e-81fc-a69a8278c7da";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ArtistDetailTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(_connection).Options);

    private async Task<int> AddArtistAsync(string name, ExternalLinkStatus? link = null)
    {
        using var db = CreateDb();
        var artist = new Artist { Name = name };
        db.Artists.Add(artist);
        if (link is { } status)
        {
            db.ArtistExternalLinks.Add(new ArtistExternalLink
            {
                Artist = artist, Source = ExternalSources.MusicBrainz, ExternalId = Mbid, Status = status,
            });
        }
        await db.SaveChangesAsync();
        return artist.Id;
    }

    [Fact]
    public async Task An_external_id_resolves_only_through_a_linked_link_never_by_name()
    {
        var linked = await AddArtistAsync("The Band", ExternalLinkStatus.Linked);
        await AddArtistAsync("Same Name Elsewhere");
        using var db = CreateDb();
        var controller = new ArtistsController(db);

        var found = await controller.ByExternal("musicbrainz", Mbid, CancellationToken.None);
        var other = await controller.ByExternal("musicbrainz", Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.Equal(linked, Assert.IsType<LinkedArtistResponse>(Assert.IsType<OkObjectResult>(found).Value).ArtistId);
        Assert.IsType<NotFoundResult>(other);
    }

    [Fact]
    public async Task An_unlinked_link_does_not_resolve_and_is_not_listed_as_a_source()
    {
        var id = await AddArtistAsync("The Band", ExternalLinkStatus.Unlinked);
        using var db = CreateDb();
        var controller = new ArtistsController(db);

        Assert.IsType<NotFoundResult>(await controller.ByExternal("musicbrainz", Mbid, CancellationToken.None));
        var detail = Assert.IsType<ArtistDetailResponse>(
            Assert.IsType<OkObjectResult>(await controller.Get(id, CancellationToken.None)).Value);
        Assert.Empty(detail.Sources);
    }

    [Fact]
    public async Task The_artist_detail_lists_linked_sources_and_unknown_artists_are_404()
    {
        var id = await AddArtistAsync("The Band", ExternalLinkStatus.Linked);
        using var db = CreateDb();
        var controller = new ArtistsController(db);

        var detail = Assert.IsType<ArtistDetailResponse>(
            Assert.IsType<OkObjectResult>(await controller.Get(id, CancellationToken.None)).Value);

        Assert.Equal("The Band", detail.Name);
        Assert.Equal(
            new LinkedArtistSource("musicbrainz", Mbid, $"https://musicbrainz.org/artist/{Mbid}"),
            Assert.Single(detail.Sources));
        Assert.IsType<NotFoundResult>(await controller.Get(id + 100, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Tracks(id + 100, cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task The_artist_tracks_are_only_that_artists_tracks()
    {
        var id = await AddArtistAsync("The Band");
        var other = await AddArtistAsync("Someone Else");
        using (var db = CreateDb())
        {
            db.Tracks.Add(new Track { Title = "Mine", Artists = [new TrackArtist { ArtistId = id }] });
            db.Tracks.Add(new Track { Title = "Theirs", Artists = [new TrackArtist { ArtistId = other }] });
            await db.SaveChangesAsync();
        }
        using var read = CreateDb();

        var result = Assert.IsType<ArtistTrackListResponse>(Assert.IsType<OkObjectResult>(
            await new ArtistsController(read).Tracks(id, cancellationToken: CancellationToken.None)).Value);

        Assert.Equal("Mine", Assert.Single(result.Items).Title);
        Assert.Equal(1, result.Total);
    }

    private sealed class FakeProvider(IReadOnlyList<ExternalTrack> tracks) : IExternalMusicSearch
    {
        public string Provider => "musicbrainz";
        public Task<IReadOnlyList<ExternalTrack>> SearchTracksAsync(string term, ExternalSearchFields fields, int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExternalArtist>> SearchArtistsAsync(string term, int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<ExternalArtist?> GetArtistAsync(string externalId, CancellationToken ct) => Task.FromResult<ExternalArtist?>(null);
        public Task<IReadOnlyList<ExternalTrack>> GetArtistTracksAsync(string externalId, int limit, CancellationToken ct) => Task.FromResult(tracks);
    }

    [Fact]
    public async Task External_tracks_are_marked_with_the_catalog_track_they_already_are_and_nothing_is_written()
    {
        int trackId;
        using (var db = CreateDb())
        {
            var track = new Track
            {
                Title = "Known",
                ExternalIds = [new TrackExternalId { Provider = "musicbrainz", ExternalId = "rec-1" }],
            };
            db.Tracks.Add(track);
            await db.SaveChangesAsync();
            trackId = track.Id;
        }
        ExternalTrack Recording(string id) => new("musicbrainz", id, id, null, "", []);
        using var read = CreateDb();
        var controller = new ExternalArtistsController(read, new FakeProvider([Recording("rec-1"), Recording("rec-2")]));

        var result = Assert.IsType<ExternalArtistTrackListResponse>(Assert.IsType<OkObjectResult>(
            await controller.Tracks(Mbid, cancellationToken: CancellationToken.None)).Value);

        Assert.Equal(trackId, result.Items[0].CatalogTrackId);
        Assert.False(result.Items[0].HasAudio);
        Assert.Null(result.Items[1].CatalogTrackId);
        Assert.Equal(1, await read.Tracks.CountAsync());
        Assert.Equal(0, await read.Artists.CountAsync());
    }

    [Fact]
    public async Task The_external_artist_endpoints_reject_a_malformed_id_and_answer_503_without_a_provider()
    {
        using var db = CreateDb();

        Assert.IsType<BadRequestObjectResult>(
            await new ExternalArtistsController(db, new FakeProvider([])).Get("not-a-uuid", CancellationToken.None));
        Assert.Equal(503, Assert.IsType<ObjectResult>(
            await new ExternalArtistsController(db).Get(Mbid, CancellationToken.None)).StatusCode);
        Assert.IsType<NotFoundResult>(
            await new ExternalArtistsController(db, new FakeProvider([])).Get(Mbid, CancellationToken.None));
    }
}
