using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using MusicHub.Api.Controllers;
using MusicHub.Api.ExternalSearch;
using MusicHub.Api.Identification;

namespace MusicHub.Api.Tests.Identification;

public class ExternalSearchTests
{
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

    [Fact]
    public async Task Tracks_carry_provider_provenance_and_stable_ids()
    {
        var handler = new StubHandler(_ => Json("""
            {"recordings":[
              {"id":"rec-1","title":"Song","disambiguation":"live",
               "artist-credit":[{"name":"The Band","artist":{"id":"art-1","name":"The Band"}}]},
              {"id":"rec-2","title":""}]}
            """));

        var tracks = await Search(handler).SearchTracksAsync("Song", new ExternalSearchFields(true, true), 5, CancellationToken.None);

        var track = Assert.Single(tracks);
        Assert.Equal("musicbrainz", track.Provider);
        Assert.Equal("rec-1", track.ExternalId);
        Assert.Equal("https://musicbrainz.org/recording/rec-1", track.Url);
        Assert.Equal("live", track.Disambiguation);
        Assert.Equal(new ExternalArtistCredit("The Band", "art-1"), Assert.Single(track.Artists));
    }

    [Fact]
    public async Task Only_the_enabled_fields_are_searched()
    {
        var handler = new StubHandler(_ => Json("{}"));
        var search = Search(handler);

        await search.SearchTracksAsync("x", new ExternalSearchFields(true, false), 5, CancellationToken.None);
        await search.SearchTracksAsync("x", new ExternalSearchFields(false, true), 5, CancellationToken.None);
        await search.SearchTracksAsync("x", new ExternalSearchFields(true, true), 5, CancellationToken.None);

        Assert.Contains("query=recording:\"x\"&", handler.Urls[0]);
        Assert.Contains("query=artist:\"x\"&", handler.Urls[1]);
        Assert.Contains("query=recording:\"x\" OR artist:\"x\"&", handler.Urls[2]);
    }

    [Fact]
    public async Task Artists_carry_provider_provenance_and_stable_ids()
    {
        var handler = new StubHandler(_ => Json("""{"artists":[{"id":"art-1","name":"The Band","disambiguation":"UK"},{"name":"No id"}]}"""));

        var artists = await Search(handler).SearchArtistsAsync("band", 5, CancellationToken.None);

        var artist = Assert.Single(artists);
        Assert.Equal(new ExternalArtist("musicbrainz", "art-1", "The Band", "UK", "https://musicbrainz.org/artist/art-1"), artist);
    }

    [Fact]
    public async Task A_rate_limited_provider_is_unavailable_not_empty()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<CandidateSearchUnavailableException>(
            () => Search(handler).SearchArtistsAsync("band", 5, CancellationToken.None));
    }

    private sealed class FailingSearch : IExternalMusicSearch
    {
        public string Provider => "stub";

        public Task<IReadOnlyList<ExternalTrack>> SearchTracksAsync(string term, ExternalSearchFields fields, int limit, CancellationToken ct) =>
            throw new CandidateSearchUnavailableException("rate limited");

        public Task<IReadOnlyList<ExternalArtist>> SearchArtistsAsync(string term, int limit, CancellationToken ct) =>
            throw new CandidateSearchUnavailableException("rate limited");

        public Task<ExternalArtist?> GetArtistAsync(string externalId, CancellationToken ct) =>
            throw new CandidateSearchUnavailableException("rate limited");

        public Task<IReadOnlyList<ExternalTrack>> GetArtistTracksAsync(string externalId, int limit, CancellationToken ct) =>
            throw new CandidateSearchUnavailableException("rate limited");
    }

    [Fact]
    public async Task One_artist_is_read_by_id_with_type_and_area_and_an_unknown_id_is_null()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/art-1")
            ? Json("""{"id":"art-1","name":"The Band","type":"Group","area":{"name":"United Kingdom"},"disambiguation":""}""")
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var search = Search(handler);

        var artist = await search.GetArtistAsync("art-1", CancellationToken.None);

        Assert.Equal(
            new ExternalArtist("musicbrainz", "art-1", "The Band", null, "https://musicbrainz.org/artist/art-1", "Group", "United Kingdom"),
            artist);
        Assert.Null(await search.GetArtistAsync("art-2", CancellationToken.None));
        Assert.Equal("/ws/2/artist/art-1?fmt=json", handler.Urls[0]);
    }

    [Fact]
    public async Task An_artists_tracks_are_browsed_by_artist_id_without_a_ranking()
    {
        var handler = new StubHandler(_ => Json("""{"recordings":[{"id":"rec-1","title":"Song"}]}"""));

        var tracks = await Search(handler).GetArtistTracksAsync("art-1", 10, CancellationToken.None);

        Assert.Equal("rec-1", Assert.Single(tracks).ExternalId);
        Assert.Equal("/ws/2/recording?artist=art-1&inc=artist-credits&limit=10&fmt=json", handler.Urls[0]);
    }

    [Fact]
    public async Task A_failing_artist_read_is_unavailable_not_missing()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        await Assert.ThrowsAsync<CandidateSearchUnavailableException>(
            () => Search(handler).GetArtistAsync("art-1", CancellationToken.None));
    }

    [Fact]
    public async Task Controller_answers_503_when_no_provider_is_configured_or_it_fails()
    {
        var none = await new ExternalSearchController().Tracks("x", null);
        var failing = await new ExternalSearchController(new FailingSearch()).Artists("x");

        Assert.Equal(503, Assert.IsType<ObjectResult>(none).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(failing).StatusCode);
    }

    [Fact]
    public async Task Controller_rejects_empty_text_unknown_fields_and_bad_limits()
    {
        var controller = new ExternalSearchController(new FailingSearch());

        Assert.IsType<BadRequestObjectResult>(await controller.Tracks("  ", null));
        Assert.IsType<BadRequestObjectResult>(await controller.Tracks("x", "album"));
        Assert.IsType<BadRequestObjectResult>(await controller.Artists("x", limit: 0));
    }
}
