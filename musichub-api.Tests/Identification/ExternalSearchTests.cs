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
