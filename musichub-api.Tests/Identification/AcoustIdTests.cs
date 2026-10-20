using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using MusicHub.Api.Identification;

namespace MusicHub.Api.Tests.Identification;

public class AcoustIdTests
{
    private sealed class StubFingerprinter(Exception? failure = null) : IChromaprintFingerprinter
    {
        public Task<AudioFingerprintData> FingerprintAsync(Stream audio, string? fileNameHint, CancellationToken ct) =>
            failure is null ? Task.FromResult(new AudioFingerprintData(180, "AQAA-fingerprint")) : Task.FromException<AudioFingerprintData>(failure);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : this((r, _) => respond(r))
        {
        }

        public string? Body { get; private set; }
        public Uri? Uri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uri = request.RequestUri;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return await respond(request, ct);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static AcoustIdOptions Options(string? key = "secret-key") => new() { ApiKey = key };

    private static AcoustIdFingerprintMatcher Matcher(
        StubHandler handler, AcoustIdOptions? options = null, IChromaprintFingerprinter? fingerprinter = null) =>
        new(new HttpClient(handler), fingerprinter ?? new StubFingerprinter(), Microsoft.Extensions.Options.Options.Create(options ?? Options()));

    private static Task<IReadOnlyList<EvidenceCandidate>> Match(AcoustIdFingerprintMatcher m) =>
        m.MatchAsync(new MemoryStream([1, 2, 3]), "song.mp3", CancellationToken.None);

    private const string OneMatch = """
        {"status":"ok","results":[{"id":"r1","score":0.97,"recordings":[
          {"id":"rec-1","title":"A Title","artists":[{"name":"An Artist"}],"releasegroups":[{"title":"The Album"}]}]}]}
        """;

    [Fact]
    public async Task A_match_yields_the_recording_with_provenance_and_the_service_score()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(OneMatch)));

        var candidates = await Match(Matcher(handler));

        var c = Assert.Single(candidates);
        Assert.Equal("A Title", c.Title);
        Assert.Equal(["An Artist"], c.Artists);
        Assert.Equal("The Album", c.Album);
        Assert.Contains("rec-1", c.Detail);
        Assert.Contains("AcoustID score 0.97", c.Detail);
    }

    [Fact]
    public async Task The_request_sends_duration_and_fingerprint_in_the_body_never_the_key_in_the_url()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(OneMatch)));

        await Match(Matcher(handler));

        Assert.Contains("duration=180", handler.Body);
        Assert.Contains("fingerprint=AQAA-fingerprint", handler.Body);
        Assert.Contains("client=secret-key", handler.Body);
        Assert.DoesNotContain("secret-key", handler.Uri!.ToString());
    }

    [Fact]
    public async Task Zero_matches_and_low_scores_yield_no_candidates()
    {
        var empty = new StubHandler(_ => Task.FromResult(Json("""{"status":"ok","results":[]}""")));
        Assert.Empty(await Match(Matcher(empty)));

        var low = new StubHandler(_ => Task.FromResult(Json(OneMatch.Replace("0.97", "0.30"))));
        Assert.Empty(await Match(Matcher(low)));
    }

    [Fact]
    public async Task Ambiguous_matches_are_all_returned_for_the_resolver_to_weigh()
    {
        var json = """
            {"status":"ok","results":[{"id":"r1","score":0.95,"recordings":[
              {"id":"rec-1","title":"A Title","artists":[{"name":"An Artist"}]},
              {"id":"rec-2","title":"A Title (Live)","artists":[{"name":"An Artist"}]},
              {"id":"rec-1","title":"A Title","artists":[{"name":"An Artist"}]}]}]}
            """;

        var candidates = await Match(Matcher(new StubHandler(_ => Task.FromResult(Json(json)))));

        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public async Task A_missing_api_key_is_unavailable_without_calling_anything()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("no call expected"));

        var ex = await Assert.ThrowsAsync<FingerprintUnavailableException>(() => Match(Matcher(handler, Options(key: " "))));

        Assert.Contains("AcoustId__ApiKey", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "rate limit")]
    [InlineData(HttpStatusCode.BadRequest, "AcoustId__ApiKey")]
    [InlineData(HttpStatusCode.InternalServerError, "status 500")]
    public async Task Service_failures_are_unavailable_and_never_leak_the_key(HttpStatusCode status, string expected)
    {
        var handler = new StubHandler(_ => Task.FromResult(Json("{}", status)));

        var ex = await Assert.ThrowsAsync<FingerprintUnavailableException>(() => Match(Matcher(handler)));

        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain("secret-key", ex.Message);
    }

    [Fact]
    public async Task An_error_status_in_the_body_is_unavailable()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json("""{"status":"error","error":{"code":14}}""")));

        var ex = await Assert.ThrowsAsync<FingerprintUnavailableException>(() => Match(Matcher(handler)));

        Assert.Contains("rate limit", ex.Message);
    }

    [Fact]
    public async Task A_timeout_is_unavailable()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json("{}");
        });
        var options = Options();
        options.TimeoutSeconds = 1;

        var ex = await Assert.ThrowsAsync<FingerprintUnavailableException>(() => Match(Matcher(handler, options)));

        Assert.Contains("did not answer", ex.Message);
    }

    [Fact]
    public async Task A_network_failure_is_unavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("boom secret-key"));

        var ex = await Assert.ThrowsAsync<FingerprintUnavailableException>(() => Match(Matcher(handler)));

        Assert.DoesNotContain("secret-key", ex.Message);
    }

    [Fact]
    public async Task A_fingerprint_failure_is_passed_on_without_calling_acoustid()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("no call expected"));
        var matcher = Matcher(handler, fingerprinter: new StubFingerprinter(new FingerprintUnavailableException("fpcalc missing")));

        var ex = await Assert.ThrowsAsync<FingerprintUnavailableException>(() => Match(matcher));

        Assert.Equal("fpcalc missing", ex.Message);
    }

    [Fact]
    public async Task The_evidence_provider_turns_an_unavailable_matcher_into_a_failed_report()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json("{}", HttpStatusCode.TooManyRequests)));
        var provider = new AudioFingerprintEvidenceProvider(Matcher(handler));
        var context = new IdentificationContext(new MemoryStream([1]), "a.mp3", null!);

        var report = await provider.CollectAsync(context, CancellationToken.None);

        Assert.Equal(EvidenceAvailability.Failed, report.Availability);
        Assert.Equal("acoustid", report.Source);
        Assert.Empty(report.Candidates);
    }
}

public class FpcalcFingerprinterTests
{
    private static FpcalcFingerprinter Create(string path, int timeoutSeconds = 30) =>
        new(Options.Create(new AcoustIdOptions { FpcalcPath = path, TimeoutSeconds = timeoutSeconds }));

    private static string Script(string body)
    {
        var path = Path.Combine(Path.GetTempPath(), "fake-fpcalc-" + Guid.NewGuid().ToString("N") + ".sh");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    [Fact]
    public async Task A_missing_fpcalc_is_unavailable()
    {
        var ex = await Assert.ThrowsAsync<FingerprintUnavailableException>(() =>
            Create("/nonexistent/fpcalc").FingerprintAsync(new MemoryStream([1, 2]), "a.mp3", CancellationToken.None));

        Assert.Contains("not installed", ex.Message);
    }

    [Fact]
    public async Task Empty_audio_is_unavailable()
    {
        await Assert.ThrowsAsync<FingerprintUnavailableException>(() =>
            Create("/nonexistent/fpcalc").FingerprintAsync(new MemoryStream(), "a.mp3", CancellationToken.None));
    }

    [Fact]
    public async Task A_staging_failure_is_unavailable()
    {
        var ex = await Assert.ThrowsAsync<FingerprintUnavailableException>(() =>
            Create("/nonexistent/fpcalc").FingerprintAsync(new FailingStream(), "a.mp3", CancellationToken.None));

        Assert.Contains("staged", ex.Message);
    }

    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("disk full");
    }

    [Fact]
    public async Task The_json_output_of_fpcalc_is_parsed()
    {
        if (OperatingSystem.IsWindows()) return;
        var script = Script("""echo '{"duration": 211.4, "fingerprint": "AQADtEm"}'""");
        try
        {
            var result = await Create(script).FingerprintAsync(new MemoryStream([1, 2]), "a.mp3", CancellationToken.None);

            Assert.Equal(211, result.DurationSeconds);
            Assert.Equal("AQADtEm", result.Fingerprint);
        }
        finally { File.Delete(script); }
    }

    [Fact]
    public async Task A_failing_fpcalc_means_unreadable_audio()
    {
        if (OperatingSystem.IsWindows()) return;
        var script = Script("echo 'ERROR: decoding failed' >&2\nexit 2");
        try
        {
            var ex = await Assert.ThrowsAsync<FingerprintUnavailableException>(() =>
                Create(script).FingerprintAsync(new MemoryStream([1, 2]), "a.mp3", CancellationToken.None));

            Assert.Contains("could not be read", ex.Message);
        }
        finally { File.Delete(script); }
    }

    [Fact]
    public async Task A_hanging_fpcalc_is_killed_after_the_timeout()
    {
        if (OperatingSystem.IsWindows()) return;
        var script = Script("exec sleep 30");
        try
        {
            var ex = await Assert.ThrowsAsync<FingerprintUnavailableException>(() =>
                Create(script, timeoutSeconds: 1).FingerprintAsync(new MemoryStream([1, 2]), "a.mp3", CancellationToken.None));

            Assert.Contains("did not finish", ex.Message);
        }
        finally { File.Delete(script); }
    }
}
