using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using MusicHub.Api.Controllers;
using MusicHub.Api.ExternalSearch;
using MusicHub.Api.Identification;

namespace MusicHub.Api.Tests.Identification;

public class ExternalAlbumTests
{
    private const string Group = "11111111-1111-1111-1111-111111111111";
    private const string Release = "22222222-2222-2222-2222-222222222222";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(Uri.UnescapeDataString(request.RequestUri!.PathAndQuery));
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static MusicBrainzCandidateSearch Search(StubHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://mb.test/ws/2/") },
            new MusicBrainzOptions { Enabled = true, Contact = "ops@example.com", MinRequestIntervalMs = 0, MaxRetries = 0 });

    private static Task<ExternalEditionTrackList?> Tracks(string releaseJson) =>
        Search(new StubHandler(_ => Json(releaseJson))).GetEditionTracksAsync(Release, CancellationToken.None);

    [Fact]
    public async Task Albums_carry_type_provenance_and_credit_and_nameless_groups_are_dropped()
    {
        var handler = new StubHandler(_ => Json("""
            {"release-groups":[
              {"id":"rg-1","title":"Studio","primary-type":"Album","secondary-types":[],"first-release-date":"1999-05",
               "artist-credit":[{"name":"The Band","artist":{"id":"art-1","name":"The Band"}}]},
              {"id":"rg-2","title":"Greatest Hits","primary-type":"Album","secondary-types":["Compilation"],
               "artist-credit":[{"name":"The Band","artist":{"id":"art-1","name":"The Band"}}]},
              {"id":"rg-3","title":""}]}
            """));

        var albums = await Search(handler).GetArtistAlbumsAsync("art-1", 10, CancellationToken.None);

        Assert.Equal(2, albums.Count);
        Assert.Equal("musicbrainz", albums[0].Provider);
        Assert.Equal("https://musicbrainz.org/release-group/rg-1", albums[0].Url);
        Assert.Equal("1999-05", albums[0].FirstReleaseDate);
        Assert.Equal(new ExternalArtistCredit("The Band", "art-1"), Assert.Single(albums[0].Artists));
        Assert.Equal(["Compilation"], albums[1].SecondaryTypes);
        Assert.Equal("/ws/2/release-group?artist=art-1&type=album&inc=artist-credits&limit=10&fmt=json", handler.Urls[0]);
    }

    [Fact]
    public async Task A_various_artists_compilation_is_found_by_title_and_keeps_its_album_credit()
    {
        var handler = new StubHandler(_ => Json("""
            {"release-groups":[{"id":"rg-1","title":"Now 50","primary-type":"Album","secondary-types":["Compilation"],
              "artist-credit":[{"name":"Various Artists","artist":{"id":"va","name":"Various Artists"}}]}]}
            """));

        var albums = await Search(handler).SearchAlbumsAsync("Now", 5, CancellationToken.None);

        Assert.Equal("Various Artists", Assert.Single(Assert.Single(albums).Artists).Name);
        Assert.Contains("query=releasegroup:\"Now\" AND primarytype:album&", handler.Urls[0]);
    }

    [Fact]
    public async Task No_albums_is_an_empty_list_and_a_failing_provider_is_unavailable()
    {
        var empty = await Search(new StubHandler(_ => Json("""{"release-groups":[]}""")))
            .SearchAlbumsAsync("nothing", 5, CancellationToken.None);

        Assert.Empty(empty);
        await Assert.ThrowsAsync<CandidateSearchUnavailableException>(() =>
            Search(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
                .SearchAlbumsAsync("x", 5, CancellationToken.None));
    }

    [Fact]
    public async Task An_album_lists_every_edition_with_its_identity_and_declared_track_count()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.Contains("release-group/")
            ? Json("""{"id":"rg-1","title":"Studio","primary-type":"Album"}""")
            : Json("""
                {"release-count":2,"releases":[
                  {"id":"rel-1","title":"Studio","date":"1999-05-01","country":"GB","status":"Official","barcode":"123",
                   "media":[{"track-count":6},{"track-count":6}]},
                  {"id":"rel-2","title":"Studio (Deluxe)","disambiguation":"deluxe","country":"XW"}]}
                """));

        var album = await Search(handler).GetAlbumEditionsAsync("rg-1", CancellationToken.None);

        Assert.NotNull(album);
        Assert.False(album.Truncated);
        Assert.Equal(["rel-1", "rel-2"], album.Editions.Select(e => e.ExternalId));
        Assert.Equal(new[] { 12, (int?)null }, album.Editions.Select(e => e.TrackCount).ToArray());
        Assert.Equal("GB", album.Editions[0].Country);
        Assert.Equal("deluxe", album.Editions[1].Disambiguation);
        Assert.Equal("https://musicbrainz.org/release/rel-1", album.Editions[0].Url);
        Assert.Equal("/ws/2/release?release-group=rg-1&inc=media&limit=100&offset=0&fmt=json", handler.Urls[1]);
    }

    [Fact]
    public async Task More_editions_than_a_page_are_read_and_more_than_the_cap_are_flagged_truncated()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.Contains("release-group/")
            ? Json("""{"id":"rg-1","title":"Studio"}""")
            : Json("""{"release-count":1000,"releases":[{"id":"rel","title":"Studio"}]}"""));

        var album = await Search(handler).GetAlbumEditionsAsync("rg-1", CancellationToken.None);

        Assert.True(album!.Truncated);
        Assert.Equal(1 + 5, handler.Urls.Count);
    }

    [Fact]
    public async Task An_unknown_album_or_edition_is_null_not_empty()
    {
        var notFound = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await Search(notFound).GetAlbumEditionsAsync("rg-x", CancellationToken.None));
        Assert.Null(await Search(notFound).GetEditionTracksAsync("rel-x", CancellationToken.None));
    }

    [Fact]
    public async Task An_ordinary_album_edition_has_its_tracks_in_order_with_their_credits()
    {
        var list = await Tracks("""
            {"id":"rel-1","title":"Studio","release-group":{"id":"rg-1"},
             "media":[{"position":1,"track-count":2,"tracks":[
               {"position":1,"number":"1","title":"First","length":181000,
                "artist-credit":[{"name":"The Band","artist":{"id":"art-1","name":"The Band"}}],
                "recording":{"id":"rec-1","title":"First"}},
               {"position":2,"number":"2","title":"Second",
                "artist-credit":[{"name":"The Band","artist":{"id":"art-1","name":"The Band"}}],
                "recording":{"id":"rec-2","title":"Second"}}]}]}
            """);

        Assert.NotNull(list);
        Assert.Equal(TrackListState.Complete, list.State);
        Assert.Empty(list.Problems);
        Assert.Equal("rg-1", list.AlbumId);
        Assert.Equal("rel-1", list.Edition.ExternalId);
        Assert.Equal(["First", "Second"], list.Tracks.Select(t => t.Title));
        Assert.Equal([1, 2], list.Tracks.Select(t => t.Position));
        Assert.Equal(181000, list.Tracks[0].LengthMs);
        Assert.Null(list.Tracks[1].LengthMs);
        Assert.Equal("https://musicbrainz.org/recording/rec-1", list.Tracks[0].RecordingUrl);
        Assert.Equal(new ExternalArtistCredit("The Band", "art-1"), Assert.Single(list.Tracks[0].Artists));
    }

    [Fact]
    public async Task A_compilation_keeps_each_tracks_own_artist_and_the_position_runs_across_discs()
    {
        var list = await Tracks("""
            {"id":"rel-1","title":"Now 50",
             "media":[
              {"position":1,"track-count":1,"tracks":[{"title":"Hit A","artist-credit":[{"name":"Alpha","artist":{"id":"a","name":"Alpha"}}],"recording":{"id":"r-a"}}]},
              {"position":2,"track-count":1,"tracks":[{"title":"Hit B","artist-credit":[{"name":"Beta","artist":{"id":"b","name":"Beta"}}],"recording":{"id":"r-b"}}]}]}
            """);

        Assert.Equal(TrackListState.Complete, list!.State);
        Assert.Equal(["Alpha", "Beta"], list.Tracks.Select(t => Assert.Single(t.Artists).Name));
        Assert.Equal([1, 2], list.Tracks.Select(t => t.Position));
        Assert.Equal([1, 2], list.Tracks.Select(t => t.Medium));
    }

    [Fact]
    public async Task A_medium_with_fewer_tracks_than_declared_is_incomplete_and_nothing_is_added()
    {
        var list = await Tracks("""
            {"id":"rel-1","title":"Studio",
             "media":[{"position":1,"track-count":3,"tracks":[{"title":"Only","recording":{"id":"r-1"}}]}]}
            """);

        Assert.Equal(TrackListState.Incomplete, list!.State);
        Assert.Single(list.Tracks);
        Assert.Contains("Medium 1 lists 1 of 3 tracks.", list.Problems);
    }

    [Fact]
    public async Task A_track_without_title_or_recording_stays_empty_and_flags_the_list()
    {
        var list = await Tracks("""
            {"id":"rel-1","title":"Studio","media":[{"position":1,"track-count":1,"tracks":[{"title":""}]}]}
            """);

        Assert.Equal(TrackListState.Incomplete, list!.State);
        var track = Assert.Single(list.Tracks);
        Assert.Null(track.Title);
        Assert.Null(track.RecordingId);
        Assert.Empty(track.Artists);
        Assert.Equal(2, list.Problems.Count);
    }

    [Fact]
    public async Task An_edition_without_tracks_is_missing_not_replaced_by_another_edition()
    {
        var handler = new StubHandler(_ => Json("""{"id":"rel-1","title":"Studio","media":[{"position":1,"track-count":0}]}"""));

        var list = await Search(handler).GetEditionTracksAsync("rel-1", CancellationToken.None);

        Assert.Equal(TrackListState.Missing, list!.State);
        Assert.Empty(list.Tracks);
        Assert.Single(handler.Urls);
    }

    [Fact]
    public void The_original_edition_is_suggested_only_when_the_album_has_exactly_one()
    {
        ExternalEdition Edition(string id) => new("musicbrainz", id, "T", null, "", null, null, null, null, null, null);
        var album = new ExternalAlbum("musicbrainz", "rg", "T", null, "", null, [], null, []);

        Assert.Equal("a", EditionSuggestion.For(new(album, [Edition("a")], false)).EditionId);
        Assert.Null(EditionSuggestion.For(new(album, [Edition("a"), Edition("b")], false)).EditionId);
        Assert.Null(EditionSuggestion.For(new(album, [Edition("a")], true)).EditionId);
        Assert.Null(EditionSuggestion.For(new(album, [], false)).EditionId);
    }

    private sealed class FakeAlbums(Func<Exception?> failure, ExternalAlbumEditions? album = null, ExternalEditionTrackList? list = null)
        : IExternalAlbumSource
    {
        public string Provider => "musicbrainz";

        public Task<IReadOnlyList<ExternalAlbum>> SearchAlbumsAsync(string term, int limit, CancellationToken ct) =>
            failure() is { } e ? throw e : Task.FromResult<IReadOnlyList<ExternalAlbum>>([]);

        public Task<IReadOnlyList<ExternalAlbum>> GetArtistAlbumsAsync(string artistExternalId, int limit, CancellationToken ct) =>
            SearchAlbumsAsync("", limit, ct);

        public Task<ExternalAlbumEditions?> GetAlbumEditionsAsync(string albumExternalId, CancellationToken ct) =>
            failure() is { } e ? throw e : Task.FromResult(album);

        public Task<ExternalEditionTrackList?> GetEditionTracksAsync(string editionExternalId, CancellationToken ct) =>
            failure() is { } e ? throw e : Task.FromResult(list);
    }

    [Fact]
    public async Task The_controller_tells_an_empty_result_from_an_unavailable_provider_and_a_missing_album()
    {
        var working = new ExternalAlbumsController(new FakeAlbums(() => null));
        var failing = new ExternalAlbumsController(new FakeAlbums(() => new CandidateSearchUnavailableException("down")));

        var empty = Assert.IsType<ExternalAlbumListResponse>(Assert.IsType<OkObjectResult>(
            await working.ArtistAlbums(Group, cancellationToken: CancellationToken.None)).Value);
        Assert.Empty(empty.Items);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await failing.Search("x")).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await failing.Tracks(Release, CancellationToken.None)).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await new ExternalAlbumsController().Editions(Group, CancellationToken.None)).StatusCode);
        Assert.IsType<NotFoundResult>(await working.Editions(Group, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await working.Tracks(Release, CancellationToken.None));
    }

    [Fact]
    public async Task The_controller_rejects_bad_input()
    {
        var controller = new ExternalAlbumsController(new FakeAlbums(() => null));

        Assert.IsType<BadRequestObjectResult>(await controller.Search("  "));
        Assert.IsType<BadRequestObjectResult>(await controller.Search("x", 0));
        Assert.IsType<BadRequestObjectResult>(await controller.ArtistAlbums("not-a-uuid"));
        Assert.IsType<BadRequestObjectResult>(await controller.Editions("not-a-uuid", CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Tracks("not-a-uuid", CancellationToken.None));
    }

    [Fact]
    public async Task The_editions_answer_carries_the_suggestion_with_its_reason()
    {
        var album = new ExternalAlbum("musicbrainz", Group, "T", null, "", null, [], null, []);
        var edition = new ExternalEdition("musicbrainz", Release, "T", null, "", null, null, null, null, null, null);
        var controller = new ExternalAlbumsController(new FakeAlbums(() => null, new ExternalAlbumEditions(album, [edition, edition], false)));

        var response = Assert.IsType<ExternalAlbumEditionsResponse>(Assert.IsType<OkObjectResult>(
            await controller.Editions(Group, CancellationToken.None)).Value);

        Assert.Equal(2, response.Editions.Count);
        Assert.Null(response.Suggestion.EditionId);
        Assert.NotEmpty(response.Suggestion.Reason);
    }
}
