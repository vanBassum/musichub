using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using MusicHub.Api.Acquisition;
using MusicHub.Api.Acquisition.Slskd;

namespace MusicHub.Api.Tests.Acquisition;

public sealed class SlskdAcquisitionProviderTests : IDisposable
{
    private readonly string _downloads = Directory.CreateTempSubdirectory("slskd-downloads").FullName;

    public void Dispose() => Directory.Delete(_downloads, recursive: true);

    private sealed record Call(HttpMethod Method, string PathAndQuery, string? Body);

    /// <summary>A stand-in for slskd's HTTP API: answers with whatever the test's script says and records every call.</summary>
    private sealed class FakeSlskd(Func<Call, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];

        public int Count(HttpMethod method, string pathStart) =>
            Calls.Count(c => c.Method == method && c.PathAndQuery.StartsWith(pathStart, StringComparison.Ordinal));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var call = new Call(request.Method, request.RequestUri!.PathAndQuery,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
            Calls.Add(call);
            return respond(call, Calls.Count(c => c.Method == call.Method && c.PathAndQuery == call.PathAndQuery));
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Empty(HttpStatusCode status = HttpStatusCode.NoContent) => new(status);

    private SlskdAcquisitionProvider Provider(FakeSlskd fake, Action<SlskdOptions>? configure = null)
    {
        var options = new SlskdOptions
        {
            BaseUrl = "http://slskd:5030",
            ApiKey = "secret-key",
            DownloadsPath = _downloads,
            SearchTimeoutSeconds = 1,
            RequestTimeoutSeconds = 5,
            DownloadTimeoutSeconds = 5,
            PollIntervalMs = 1,
        };
        configure?.Invoke(options);
        return new SlskdAcquisitionProvider(
            new HttpClient(fake) { BaseAddress = new Uri("http://slskd:5030/") }, Options.Create(options));
    }

    private static readonly AcquisitionQuery Query = new("An Artist", "A Title");

    private const string Responses = """
        [
          {"username":"peer1","hasFreeUploadSlot":true,"queueLength":0,"files":[
            {"filename":"@@share\\Music\\An Artist\\Album\\01 A Title.mp3","size":4000000,"bitRate":320,"length":215,"sampleRate":44100},
            {"filename":"@@share\\Music\\An Artist\\Album\\cover.jpg","size":90000},
            {"filename":"@@share\\Music\\An Artist\\Album\\notes.txt","size":10}]},
          {"username":"peer2","hasFreeUploadSlot":false,"queueLength":7,"files":[
            {"filename":"/music/A Title.FLAC","size":30000000,"sampleRate":96000,"bitDepth":24,"length":216}]}
        ]
        """;

    private static HttpResponseMessage SearchScript(Call call, int _) => call switch
    {
        { Method.Method: "POST" } => Json("{}"),
        { Method.Method: "DELETE" } => Empty(),
        _ when call.PathAndQuery.EndsWith("/responses") => Json(Responses),
        _ => Json("""{"isComplete":true}"""),
    };

    [Fact]
    public async Task A_search_yields_only_audio_files_with_the_peers_metadata_in_slskds_order()
    {
        var fake = new FakeSlskd(SearchScript);

        var candidates = await Provider(fake).SearchAsync(Query, CancellationToken.None);

        Assert.Equal(2, candidates.Count);
        var mp3 = candidates[0];
        Assert.Equal("slskd", mp3.Provider);
        Assert.Equal("peer1", mp3.Source);
        Assert.Equal("01 A Title.mp3", mp3.FileName);
        Assert.Equal("mp3", mp3.Format);
        Assert.Equal((320, 215, true, 0), (mp3.BitrateKbps, mp3.DurationSeconds, mp3.HasFreeSlot, mp3.QueueLength));
        var flac = candidates[1];
        Assert.Equal("A Title.FLAC", flac.FileName);
        Assert.Equal("flac", flac.Format);
        Assert.Equal((96000, 24, false, 7), (flac.SampleRateHz, flac.BitDepth, flac.HasFreeSlot, flac.QueueLength));
    }

    [Fact]
    public async Task A_search_sends_artist_and_title_as_text_and_removes_the_search_afterwards()
    {
        var fake = new FakeSlskd(SearchScript);

        await Provider(fake).SearchAsync(Query, CancellationToken.None);

        Assert.Contains("\"searchText\":\"An Artist A Title\"", fake.Calls[0].Body);
        Assert.Equal(1, fake.Count(HttpMethod.Delete, "/api/v0/searches/"));
    }

    [Fact]
    public async Task A_search_caps_the_candidates()
    {
        var fake = new FakeSlskd(SearchScript);

        var candidates = await Provider(fake, o => o.MaxCandidates = 1).SearchAsync(Query, CancellationToken.None);

        Assert.Single(candidates);
    }

    [Fact]
    public async Task A_search_without_answers_is_empty_not_a_failure()
    {
        var fake = new FakeSlskd((call, n) => call.PathAndQuery.EndsWith("/responses") ? Json("[]") : SearchScript(call, n));

        Assert.Empty(await Provider(fake).SearchAsync(Query, CancellationToken.None));
    }

    [Fact]
    public async Task An_unreachable_slskd_fails_the_search_with_the_setting_to_check()
    {
        var fake = new FakeSlskd((_, _) => throw new HttpRequestException("connection refused"));

        var e = await Assert.ThrowsAsync<AcquisitionUnavailableException>(
            () => Provider(fake).SearchAsync(Query, CancellationToken.None));

        Assert.Contains("Slskd__BaseUrl", e.Message);
    }

    [Fact]
    public async Task A_rejected_api_key_names_the_setting_and_never_the_key()
    {
        var fake = new FakeSlskd((_, _) => Empty(HttpStatusCode.Unauthorized));

        var e = await Assert.ThrowsAsync<AcquisitionUnavailableException>(
            () => Provider(fake).SearchAsync(Query, CancellationToken.None));

        Assert.Contains("Slskd__ApiKey", e.Message);
        Assert.DoesNotContain("secret-key", e.Message);
    }

    [Fact]
    public async Task A_slskd_that_does_not_answer_in_time_fails_the_search()
    {
        var provider = new SlskdAcquisitionProvider(
            new HttpClient(new SlowHandler()) { BaseAddress = new Uri("http://slskd:5030/") },
            Options.Create(new SlskdOptions { RequestTimeoutSeconds = 1, SearchTimeoutSeconds = 1, PollIntervalMs = 1 }));

        var e = await Assert.ThrowsAsync<AcquisitionUnavailableException>(
            () => provider.SearchAsync(Query, CancellationToken.None));

        Assert.Contains("did not answer within 1 seconds", e.Message);
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Empty();
        }
    }

    [Fact]
    public async Task A_cancelled_search_stops_and_still_removes_the_search_in_slskd()
    {
        using var cts = new CancellationTokenSource();
        var fake = new FakeSlskd((call, n) =>
        {
            if (call.Method == HttpMethod.Get && !call.PathAndQuery.EndsWith("/responses"))
            {
                cts.Cancel();
                return Json("""{"isComplete":false}""");
            }
            return SearchScript(call, n);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(fake).SearchAsync(Query, cts.Token));

        Assert.Equal(1, fake.Count(HttpMethod.Delete, "/api/v0/searches/"));
    }

    [Fact]
    public async Task A_search_needs_an_artist_or_a_title()
    {
        var fake = new FakeSlskd(SearchScript);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Provider(fake).SearchAsync(new AcquisitionQuery(" ", ""), CancellationToken.None));
        Assert.Empty(fake.Calls);
    }

    private static readonly AcquisitionCandidate Candidate = new(
        "slskd", "peer1", @"@@share\Music\An Artist\Album\01 A Title.mp3", 4_000_000);

    private static string Transfers(string id, string state, string path) => $$"""
        {"username":"peer1","directories":[{"directory":"x","files":[
          {"id":"{{id}}","filename":{{System.Text.Json.JsonSerializer.Serialize(path)}},"state":"{{state}}"}]}]}
        """;

    private const string NoTransfers = """{"username":"peer1","directories":[]}""";

    private static bool IsTransferList(Call c) =>
        c.Method == HttpMethod.Get && c.PathAndQuery == "/api/v0/transfers/downloads/peer1";

    /// <summary>A download script: no transfer before the request is queued, then each state in turn.</summary>
    private FakeSlskd DownloadScript(AcquisitionCandidate candidate, params string[] states)
    {
        FakeSlskd? self = null;
        self = new FakeSlskd((call, _) =>
        {
            if (call.Method == HttpMethod.Post)
            {
                return Empty(HttpStatusCode.Created);
            }
            if (call.Method == HttpMethod.Delete)
            {
                return Empty();
            }
            var polls = self!.Calls.Count(c => IsTransferList(c));
            if (self.Count(HttpMethod.Post, "/api/v0/transfers/downloads/peer1") == 0)
            {
                return Json(NoTransfers);
            }
            return Json(Transfers("t1", states[Math.Min(polls - 2, states.Length - 1)], candidate.Path));
        });
        return self;
    }

    private void StoreDownloadedFile()
    {
        Directory.CreateDirectory(Path.Combine(_downloads, "Album"));
        File.WriteAllBytes(Path.Combine(_downloads, "Album", "01 A Title.mp3"), [1, 2, 3]);
    }

    [Fact]
    public async Task A_completed_download_returns_the_file_from_the_downloads_folder()
    {
        StoreDownloadedFile();
        var fake = DownloadScript(Candidate, "Queued, Remotely", "InProgress", "Completed, Succeeded");

        var outcome = await Provider(fake).DownloadAsync(Candidate, CancellationToken.None);

        Assert.NotNull(outcome.File);
        Assert.Equal("01 A Title.mp3", outcome.File.FileName);
        await using var content = outcome.File.OpenRead();
        Assert.Equal(3, content.Length);
        Assert.Contains("\"filename\":\"@@share\\\\Music\\\\An Artist\\\\Album\\\\01 A Title.mp3\"", fake.Calls.Single(c => c.Method == HttpMethod.Post).Body);
    }

    [Fact]
    public async Task A_download_the_source_declines_is_reported_as_rejected()
    {
        var outcome = await Provider(DownloadScript(Candidate, "Completed, Rejected")).DownloadAsync(Candidate, CancellationToken.None);

        Assert.Equal(AcquisitionFailure.Rejected, outcome.Failure);
        Assert.Contains("peer1", outcome.Detail);
    }

    [Theory]
    [InlineData("Completed, Errored")]
    [InlineData("Completed, Cancelled")]
    [InlineData("Completed, TimedOut")]
    public async Task A_transfer_that_ends_without_the_file_is_reported_with_slskds_state(string state)
    {
        var outcome = await Provider(DownloadScript(Candidate, state)).DownloadAsync(Candidate, CancellationToken.None);

        Assert.Equal(AcquisitionFailure.TransferFailed, outcome.Failure);
        Assert.Contains(state, outcome.Detail);
    }

    [Fact]
    public async Task A_transfer_left_over_from_an_earlier_download_of_the_same_file_is_not_ours()
    {
        StoreDownloadedFile();
        var fake = new FakeSlskd((call, _) =>
        {
            if (call.Method == HttpMethod.Post)
            {
                return Empty(HttpStatusCode.Created);
            }
            return Json(Transfers("old", "Completed, Errored", Candidate.Path));
        });

        // The old failed transfer stays; ours never appears, so the download ends at its bound, not with the old result.
        var outcome = await Provider(fake, o => o.DownloadTimeoutSeconds = 1).DownloadAsync(Candidate, CancellationToken.None);

        Assert.Equal(AcquisitionFailure.TimedOut, outcome.Failure);
    }

    [Fact]
    public async Task A_download_that_does_not_finish_in_time_is_cancelled_in_slskd_and_reported_as_timed_out()
    {
        var fake = DownloadScript(Candidate, "InProgress");

        var outcome = await Provider(fake, o => o.DownloadTimeoutSeconds = 1).DownloadAsync(Candidate, CancellationToken.None);

        Assert.Equal(AcquisitionFailure.TimedOut, outcome.Failure);
        Assert.Contains("1 seconds", outcome.Detail);
        Assert.Equal(1, fake.Count(HttpMethod.Delete, "/api/v0/transfers/downloads/peer1/t1?remove=true"));
    }

    [Fact]
    public async Task A_cancelled_download_cancels_the_transfer_in_slskd_and_throws()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var fake = DownloadScript(Candidate, "InProgress");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(fake).DownloadAsync(Candidate, cts.Token));

        Assert.Equal(1, fake.Count(HttpMethod.Delete, "/api/v0/transfers/downloads/peer1/t1?remove=true"));
    }

    [Fact]
    public async Task A_finished_download_missing_from_the_downloads_folder_says_what_to_check()
    {
        var outcome = await Provider(DownloadScript(Candidate, "Completed, Succeeded")).DownloadAsync(Candidate, CancellationToken.None);

        Assert.Equal(AcquisitionFailure.FileNotAccessible, outcome.Failure);
        Assert.Contains("Slskd__DownloadsPath", outcome.Detail);
    }

    [Fact]
    public async Task Without_a_downloads_path_a_finished_download_cannot_be_read()
    {
        var outcome = await Provider(DownloadScript(Candidate, "Completed, Succeeded"), o => o.DownloadsPath = null)
            .DownloadAsync(Candidate, CancellationToken.None);

        Assert.Equal(AcquisitionFailure.FileNotAccessible, outcome.Failure);
        Assert.Contains("Slskd__DownloadsPath", outcome.Detail);
    }

    [Fact]
    public async Task A_remote_path_cannot_point_outside_the_downloads_folder()
    {
        var inner = Path.Combine(_downloads, "inner");
        Directory.CreateDirectory(inner);
        File.WriteAllBytes(Path.Combine(_downloads, "stolen.mp3"), [1]);
        var escaping = Candidate with { Path = @"..\stolen.mp3" };

        var outcome = await Provider(DownloadScript(escaping, "Completed, Succeeded"), o => o.DownloadsPath = inner)
            .DownloadAsync(escaping, CancellationToken.None);

        Assert.Equal(AcquisitionFailure.FileNotAccessible, outcome.Failure);
    }

    [Fact]
    public async Task A_download_slskd_refuses_to_queue_fails_as_unavailable()
    {
        var fake = new FakeSlskd((call, _) => call.Method == HttpMethod.Post
            ? Empty(HttpStatusCode.Forbidden)
            : Json(NoTransfers));

        var outcome = await Provider(fake).DownloadAsync(Candidate, CancellationToken.None);

        Assert.Equal(AcquisitionFailure.Unavailable, outcome.Failure);
        Assert.Contains("Slskd__ApiKey", outcome.Detail);
    }

    [Fact]
    public async Task An_unreachable_slskd_fails_the_download_as_unavailable()
    {
        var fake = new FakeSlskd((_, _) => throw new HttpRequestException("connection refused"));

        var outcome = await Provider(fake).DownloadAsync(Candidate, CancellationToken.None);

        Assert.Equal(AcquisitionFailure.Unavailable, outcome.Failure);
        Assert.Contains("Slskd__BaseUrl", outcome.Detail);
    }
}
