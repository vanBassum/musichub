using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using MusicHub.Api.Controllers;
using MusicHub.Api.Data;
using MusicHub.Api.ExternalSearch;
using MusicHub.Api.Identification;

namespace MusicHub.Api.Tests.Identification;

public sealed class ListenBrainzProviderTests : IDisposable
{
    private const string Artist = "5b11f4ce-a62d-471e-81fc-a69a8278c7da";
    private const string Rec1 = "11111111-1111-1111-1111-111111111111";
    private const string Rec2 = "22222222-2222-2222-2222-222222222222";

    private sealed class StubHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(responses[Math.Min(Urls.Count, responses.Length) - 1](request));
        }
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Json(string body) =>
        _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Func<HttpRequestMessage, HttpResponseMessage> Status(HttpStatusCode code) =>
        _ => new HttpResponseMessage(code);

    private readonly MemoryCache cache = new(new MemoryCacheOptions());

    public void Dispose() => cache.Dispose();

    private ListenBrainzProvider Provider(StubHandler handler, int cacheMinutes = 60) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://lb.test/1/") },
            new ListenBrainzOptions { Enabled = true, MinRequestIntervalMs = 0, RetryDelayMs = 1, MaxRetryDelayMs = 5, MaxRetries = 1, CacheMinutes = cacheMinutes },
            cache);

    private static string Entry(string recording, string? listens = "10", string users = "3") =>
        $$"""{"recording_mbid":"{{recording}}","recording_name":"Song","total_listen_count":{{listens ?? "null"}},"total_user_count":{{users}}}""";

    [Fact]
    public async Task Popularity_is_mapped_by_musicbrainz_recording_id_and_unusable_entries_are_dropped()
    {
        var handler = new StubHandler(Json($$"""
            [{{Entry(Rec1, "1200", "300")}},
             {{Entry(Rec1, "5")}},
             {{Entry(Rec2, null)}},
             {"recording_name":"No id","total_listen_count":7},
             {{Entry("not-a-uuid")}}]
            """));

        var popularity = await Provider(handler).GetArtistPopularityAsync(Artist, CancellationToken.None);

        Assert.Equal(new ExternalTrackPopularity("listenbrainz", Rec1, 1200, 300), Assert.Single(popularity));
        Assert.Equal($"/1/popularity/top-recordings-for-artist/{Artist}", handler.Urls[0]);
    }

    [Fact]
    public async Task An_unknown_artist_or_a_foreign_identifier_is_empty_not_a_failure()
    {
        var handler = new StubHandler(Status(HttpStatusCode.NotFound));
        var provider = Provider(handler);

        Assert.Empty(await provider.GetArtistPopularityAsync(Artist, CancellationToken.None));
        Assert.Empty(await provider.GetArtistPopularityAsync("../../token", CancellationToken.None));
        Assert.Single(handler.Urls);
    }

    [Fact]
    public async Task A_transient_failure_is_retried_and_a_persistent_one_is_unavailable_not_empty()
    {
        var retried = new StubHandler(Status(HttpStatusCode.TooManyRequests), Json($"[{Entry(Rec1)}]"));
        Assert.Single(await Provider(retried, cacheMinutes: 0).GetArtistPopularityAsync(Artist, CancellationToken.None));
        Assert.Equal(2, retried.Urls.Count);

        var failing = new StubHandler(Status(HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<CandidateSearchUnavailableException>(
            () => Provider(failing, cacheMinutes: 0).GetArtistPopularityAsync(Artist, CancellationToken.None));
    }

    [Fact]
    public async Task A_malformed_answer_is_unavailable()
    {
        await Assert.ThrowsAsync<CandidateSearchUnavailableException>(
            () => Provider(new StubHandler(Json("""{"error":"x"}"""))).GetArtistPopularityAsync(Artist, CancellationToken.None));
        await Assert.ThrowsAsync<CandidateSearchUnavailableException>(
            () => Provider(new StubHandler(Json("<html>"))).GetArtistPopularityAsync(Artist, CancellationToken.None));
    }

    [Fact]
    public async Task Answers_are_cached_per_artist_and_failures_are_not()
    {
        var handler = new StubHandler(Status(HttpStatusCode.BadRequest), Json($"[{Entry(Rec1)}]"));
        var provider = Provider(handler);

        await Assert.ThrowsAsync<CandidateSearchUnavailableException>(
            () => provider.GetArtistPopularityAsync(Artist, CancellationToken.None));
        await provider.GetArtistPopularityAsync(Artist, CancellationToken.None);
        await provider.GetArtistPopularityAsync(Artist, CancellationToken.None);

        Assert.Equal(2, handler.Urls.Count);
    }

    private sealed class FakeIdentity(params ExternalTrack[] tracks) : IExternalMusicSearch
    {
        public string Provider => "musicbrainz";
        public Task<IReadOnlyList<ExternalTrack>> SearchTracksAsync(string term, ExternalSearchFields fields, int limit, int offset, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExternalArtist>> SearchArtistsAsync(string term, int limit, int offset, CancellationToken ct) => throw new NotSupportedException();
        public Task<ExternalArtist?> GetArtistAsync(string externalId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExternalTrack>> GetArtistTracksAsync(string externalId, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExternalTrack>>(tracks);
    }

    private static ExternalTrack Recording(string id) => new("musicbrainz", id, id, null, "", []);

    private async Task<ExternalArtistTrackListResponse> ListTracksAsync(IExternalPopularitySource popularity)
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = new MusicHubDbContext(new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        var controller = new ExternalArtistsController(db, new FakeIdentity(Recording(Rec1), Recording(Rec2)), popularity);

        return Assert.IsType<ExternalArtistTrackListResponse>(Assert.IsType<OkObjectResult>(
            await controller.Tracks(Artist, cancellationToken: CancellationToken.None)).Value);
    }

    [Fact]
    public async Task Tracks_carry_popularity_only_where_the_source_has_it_and_no_score_is_made_up()
    {
        var provider = Provider(new StubHandler(Json($"[{Entry(Rec1, "40")}]")));

        var result = await ListTracksAsync(provider);

        Assert.Equal(new ExternalPopularityStatus("listenbrainz", PopularityAvailability.Provided), result.Popularity);
        Assert.Equal(40, result.Items[0].Popularity!.ListenCount);
        Assert.Null(result.Items[1].Popularity);
    }

    [Fact]
    public async Task A_failing_popularity_source_does_not_stop_the_track_list_and_is_reported()
    {
        var provider = Provider(new StubHandler(Status(HttpStatusCode.InternalServerError)), cacheMinutes: 0);

        var result = await ListTracksAsync(provider);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.Null(i.Popularity));
        Assert.Equal(PopularityAvailability.Unavailable, result.Popularity!.Availability);
    }

    [Fact]
    public async Task A_source_without_popularity_is_reported_as_nothing_found()
    {
        var result = await ListTracksAsync(Provider(new StubHandler(Json("[]"))));

        Assert.Equal(PopularityAvailability.NothingFound, result.Popularity!.Availability);
    }

    [Fact]
    public void The_controller_is_created_without_the_optional_popularity_source()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        using var db = new MusicHubDbContext(new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(connection).Options);
        var services = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton<IExternalMusicSearch>(new FakeIdentity())
            .BuildServiceProvider();

        Assert.NotNull(ActivatorUtilities.CreateInstance<ExternalArtistsController>(services));

        var keyed = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton<IExternalPopularitySource>(Provider(new StubHandler(Json("[]"))))
            .BuildServiceProvider();
        Assert.NotNull(ActivatorUtilities.CreateInstance<ExternalArtistsController>(keyed));
    }
}
