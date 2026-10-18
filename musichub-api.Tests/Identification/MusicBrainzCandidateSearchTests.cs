using System.Diagnostics;
using System.Net;
using System.Text;
using MusicHub.Api.Identification;

namespace MusicHub.Api.Tests.Identification;

public class MusicBrainzCandidateSearchTests
{
    private sealed class StubHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var respond = responses[Math.Min(Requests.Count, responses.Length) - 1];
            return Task.FromResult(respond(request));
        }
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Json(string body) =>
        _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Func<HttpRequestMessage, HttpResponseMessage> Status(HttpStatusCode code) =>
        _ => new HttpResponseMessage(code);

    private static MusicBrainzOptions FastOptions() => new()
    {
        Enabled = true,
        Contact = "ops@example.com",
        MinRequestIntervalMs = 0,
        RetryDelayMs = 1,
        MaxRetryDelayMs = 5,
    };

    private static MusicBrainzCandidateSearch Search(StubHandler handler, MusicBrainzOptions? options = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://mb.test/ws/2/") };
        return new MusicBrainzCandidateSearch(http, options ?? FastOptions());
    }

    private const string TwoRecordings = """
        {"recordings":[
          {"id":"rec-1","score":100,"title":"Song","disambiguation":"live",
           "artist-credit":[{"name":"The Band","artist":{"name":"The Band"}}],
           "releases":[
             {"id":"rel-1","title":"Studio Album","status":"Official","date":"2001-05-01","release-group":{"primary-type":"Album"}},
             {"id":"rel-2","title":"Greatest Hits","status":"Official"}]},
          {"id":"rec-2","score":90,"title":"Song","artist-credit":[{"name":"The Band"}],"releases":[]},
          {"id":"rec-3","title":"","artist-credit":[{"name":"X"}]}
        ]}
        """;

    [Fact]
    public async Task Every_recording_and_release_becomes_a_candidate_with_source_identifiers()
    {
        var handler = new StubHandler(Json(TwoRecordings));

        var candidates = await Search(handler).SearchAsync("Song", ["The Band"], CancellationToken.None);

        Assert.Equal(3, candidates.Count);
        var first = candidates[0];
        Assert.Equal("Studio Album", first.Album);
        Assert.Equal("rec-1", first.Reference!.RecordingId);
        Assert.Equal("https://musicbrainz.org/recording/rec-1", first.Reference.RecordingUrl);
        Assert.Equal("https://musicbrainz.org/release/rel-1", first.Reference.ReleaseUrl);
        Assert.Contains("disambiguation \"live\"", first.Reference.Detail);
        Assert.Contains("release date 2001-05-01", first.Reference.Detail);
        Assert.Contains("release type Album", first.Reference.Detail);
        Assert.Equal("rec-1", candidates[1].Reference!.RecordingId);
        Assert.Equal("rel-2", candidates[1].Reference!.ReleaseId);
        Assert.Equal("rec-2", candidates[2].Reference!.RecordingId);
        Assert.Null(candidates[2].Album);
        Assert.DoesNotContain(candidates, c => c.Reference!.RecordingId == "rec-3");
    }

    [Fact]
    public async Task The_query_carries_the_tags_as_quoted_hints()
    {
        var handler = new StubHandler(Json("""{"recordings":[]}"""));

        await Search(handler).SearchAsync("Say \"Hi\"", ["A", "B"], CancellationToken.None);

        var query = Uri.UnescapeDataString(handler.Requests[0].RequestUri!.Query);
        Assert.Contains("recording:\"Say \\\"Hi\\\"\" AND artist:\"A\" AND artist:\"B\"", query);
        Assert.Contains("fmt=json", query);
    }

    [Fact]
    public async Task No_recordings_is_an_empty_result()
    {
        var candidates = await Search(new StubHandler(Json("""{"recordings":[]}"""))).SearchAsync("S", ["A"], CancellationToken.None);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task Transient_failures_are_retried()
    {
        var handler = new StubHandler(Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.TooManyRequests), Json(TwoRecordings));

        var candidates = await Search(handler).SearchAsync("Song", ["The Band"], CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(3, candidates.Count);
    }

    [Fact]
    public async Task Failures_that_persist_are_unavailable_not_empty()
    {
        var handler = new StubHandler(Status(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<CandidateSearchUnavailableException>(
            () => Search(handler).SearchAsync("Song", ["The Band"], CancellationToken.None));

        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task A_client_error_is_not_retried()
    {
        var handler = new StubHandler(Status(HttpStatusCode.BadRequest));

        await Assert.ThrowsAsync<CandidateSearchUnavailableException>(
            () => Search(handler).SearchAsync("Song", ["The Band"], CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Garbage_is_unavailable()
    {
        await Assert.ThrowsAsync<CandidateSearchUnavailableException>(
            () => Search(new StubHandler(Json("<html>"))).SearchAsync("Song", ["The Band"], CancellationToken.None));
    }

    [Fact]
    public async Task Requests_are_spaced_by_the_minimum_interval()
    {
        var options = FastOptions();
        options.MinRequestIntervalMs = 100;
        var search = Search(new StubHandler(Json("""{"recordings":[]}""")), options);

        var timer = Stopwatch.StartNew();
        await search.SearchAsync("S", ["A"], CancellationToken.None);
        await search.SearchAsync("S", ["A"], CancellationToken.None);
        await search.SearchAsync("S", ["A"], CancellationToken.None);

        Assert.True(timer.ElapsedMilliseconds >= 180, $"Took only {timer.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public async Task The_provider_reports_a_failed_search_as_unavailable()
    {
        var provider = new CandidateSearchEvidenceProvider(Search(new StubHandler(Status(HttpStatusCode.InternalServerError))));
        var report = await provider.CollectAsync(Context(), CancellationToken.None);

        Assert.Equal(EvidenceAvailability.Unavailable, report.Availability);
        Assert.Empty(report.Candidates);
        Assert.Equal("musicbrainz", report.Source);
    }

    [Fact]
    public async Task The_provider_reports_an_empty_search_as_nothing_found()
    {
        var provider = new CandidateSearchEvidenceProvider(Search(new StubHandler(Json("""{"recordings":[]}"""))));
        var report = await provider.CollectAsync(Context(), CancellationToken.None);

        Assert.Equal(EvidenceAvailability.NothingFound, report.Availability);
    }

    [Fact]
    public async Task The_provider_keeps_ambiguous_candidates_apart_in_the_evidence()
    {
        var provider = new CandidateSearchEvidenceProvider(Search(new StubHandler(Json(TwoRecordings))));
        var report = await provider.CollectAsync(Context(), CancellationToken.None);

        Assert.Equal(EvidenceAvailability.Provided, report.Availability);
        var descriptions = report.Candidates.Select(c => $"{c.Album} {c.Reference!.RecordingUrl} {c.Reference.ReleaseUrl}").ToList();
        Assert.Contains(descriptions, d => d.Contains("recording/rec-1") && d.Contains("release/rel-1"));
        Assert.Contains(descriptions, d => d.Contains("recording/rec-2"));
    }

    [Fact]
    public void Enabling_without_contact_fails_loudly()
    {
        var options = FastOptions();
        options.Contact = " ";

        Assert.Throws<InvalidOperationException>(() => MusicBrainzCandidateSearch.Create(options, new NoFactory()));
    }

    private sealed class NoFactory(HttpClient? client = null) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client ?? new();
    }

    [Fact]
    public void User_agent_names_the_app_and_the_contact()
    {
        var client = new HttpClient();

        MusicBrainzCandidateSearch.Create(FastOptions(), new NoFactory(client));

        Assert.Equal("MusicHub/1.0 ( ops@example.com )", client.DefaultRequestHeaders.UserAgent.ToString());
    }

    private static IdentificationContext Context() =>
        new(new MemoryStream(),
            "song.mp3",
            new MusicHub.Api.Metadata.AudioMetadata(
                new MusicHub.Api.Metadata.AudioTechnicalProperties(null, null, null, null, null, null, null),
                new MusicHub.Api.Metadata.AudioTags("Song", ["The Band"], null, null, null, null, null, null, [])));
}
