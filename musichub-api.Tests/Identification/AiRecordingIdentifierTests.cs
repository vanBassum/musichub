using MusicHub.Api.Identification;
using MusicHub.Api.Metadata;

namespace MusicHub.Api.Tests.Identification;

public class AiRecordingIdentifierTests
{
    private sealed class FakeProvider(string source, params EvidenceCandidate[] candidates) : IIdentificationEvidenceProvider
    {
        public int Runs { get; private set; }
        public string Source { get; } = source;

        public Task<EvidenceReport> CollectAsync(IdentificationContext context, CancellationToken ct)
        {
            Runs++;
            return Task.FromResult(candidates.Length == 0
                ? EvidenceReport.NothingFound(Source, "nothing")
                : EvidenceReport.Provided(Source, candidates, "found"));
        }
    }

    private sealed class ScriptedClient(params Func<AiIdentificationRequest, AiIdentificationAction>[] script) : IIdentificationAiClient
    {
        public List<AiIdentificationRequest> Requests { get; } = [];

        public Task<AiIdentificationAction> NextActionAsync(AiIdentificationRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var next = script[Math.Min(Requests.Count, script.Length) - 1];
            return Task.FromResult(next(request));
        }
    }

    private sealed class ThrowingClient : IIdentificationAiClient
    {
        public Task<AiIdentificationAction> NextActionAsync(AiIdentificationRequest request, CancellationToken ct) =>
            throw new HttpRequestException("model down");
    }

    private static readonly EvidenceCandidate Song = new("Song", ["Artist"], "Album");
    private static readonly EvidenceCandidate Other = new("Other Song", ["Someone"], null);

    private static Task<IdentificationResult> Run(
        IIdentificationAiClient? client, int maxSteps, params IIdentificationEvidenceProvider[] providers)
    {
        var identifier = new AiRecordingIdentifier("some-model", maxSteps, client, new TagLibAudioMetadataReader(), providers);
        var stream = new MemoryStream(AudioSamples.Mp3());
        return identifier.IdentifyAsync(stream, "song.mp3", CancellationToken.None);
    }

    private static AiIdentificationAction RunTool(string tool) => new(AiActionKind.RunTool, Tool: tool);

    private static AiIdentificationAction Propose(string id) => new(AiActionKind.Propose, CandidateId: id);

    [Fact]
    public async Task Unavailable_ai_leaves_the_file_unresolved()
    {
        var provider = new FakeProvider("search", Song);

        var result = await Run(null, 3, provider);

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
        Assert.Null(result.Identity);
        Assert.Equal(0, provider.Runs);
    }

    [Fact]
    public async Task A_failing_client_leaves_the_file_unresolved()
    {
        var result = await Run(new ThrowingClient(), 3, new FakeProvider("search", Song));

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
        Assert.Contains("model down", result.UnresolvedReason);
    }

    [Fact]
    public async Task Proposes_a_candidate_a_tool_produced()
    {
        var client = new ScriptedClient(_ => RunTool("search"), r => Propose(r.Outcomes[0].Candidates[0].Id));

        var result = await Run(client, 3, new FakeProvider("search", Song), new FakeProvider("fingerprint"));

        Assert.Equal(IdentificationStatus.Identified, result.Status);
        Assert.Equal("Song", result.Identity!.Title);
        Assert.Equal(["Artist"], result.Identity.Artists);
        Assert.Contains(result.Evidence, e => e.Source == "search");
        Assert.Equal(["search", "fingerprint"], client.Requests[0].AvailableTools);
        Assert.Equal(["fingerprint"], client.Requests[1].AvailableTools);
    }

    [Fact]
    public async Task Only_the_tools_the_ai_chose_run()
    {
        var unused = new FakeProvider("fingerprint", Other);
        var client = new ScriptedClient(_ => RunTool("search"), _ => Propose("c1"));

        await Run(client, 3, new FakeProvider("search", Song), unused);

        Assert.Equal(0, unused.Runs);
    }

    [Fact]
    public async Task Rejects_an_unknown_candidate_id()
    {
        var client = new ScriptedClient(_ => RunTool("search"), _ => Propose("c99"));

        var result = await Run(client, 3, new FakeProvider("search", Song));

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
        Assert.Null(result.Identity);
        Assert.Contains("c99", result.UnresolvedReason);
    }

    [Fact]
    public async Task Rejects_a_proposal_before_any_tool_produced_a_candidate()
    {
        var result = await Run(new ScriptedClient(_ => Propose("c1")), 3, new FakeProvider("search", Song));

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
    }

    [Fact]
    public async Task Rejects_an_unknown_tool()
    {
        var result = await Run(new ScriptedClient(_ => RunTool("made-up")), 3, new FakeProvider("search", Song));

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
        Assert.Contains("made-up", result.UnresolvedReason);
    }

    [Fact]
    public async Task Rejects_running_the_same_tool_twice()
    {
        var provider = new FakeProvider("search", Song);

        var result = await Run(new ScriptedClient(_ => RunTool("search")), 3, provider);

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
        Assert.Equal(1, provider.Runs);
    }

    [Fact]
    public async Task Rejects_a_malformed_action()
    {
        var client = new ScriptedClient(_ => new AiIdentificationAction((AiActionKind)42));

        var result = await Run(client, 3, new FakeProvider("search", Song));

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
    }

    [Fact]
    public async Task Keeps_conflicting_evidence_and_lets_the_ai_choose_by_id()
    {
        var client = new ScriptedClient(
            _ => RunTool("search"),
            _ => RunTool("fingerprint"),
            r =>
            {
                var all = r.Outcomes.SelectMany(o => o.Candidates).ToList();
                Assert.Equal(2, all.Count);
                Assert.NotEqual(all[0].Id, all[1].Id);
                return Propose(all[1].Id);
            });

        var result = await Run(client, 3, new FakeProvider("search", Song), new FakeProvider("fingerprint", Other));

        Assert.Equal(IdentificationStatus.Identified, result.Status);
        Assert.Equal("Other Song", result.Identity!.Title);
        Assert.Contains(result.Evidence, e => e.Source == "search");
        Assert.Contains(result.Evidence, e => e.Source == "fingerprint");
    }

    [Fact]
    public async Task Giving_up_is_unresolved_with_the_reason()
    {
        var client = new ScriptedClient(_ => new AiIdentificationAction(AiActionKind.GiveUp, Reason: "evidence conflicts"));

        var result = await Run(client, 3, new FakeProvider("search", Song));

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
        Assert.Contains("evidence conflicts", result.UnresolvedReason);
    }

    [Fact]
    public async Task Stops_when_the_tool_loop_limit_is_reached()
    {
        var a = new FakeProvider("a", Song);
        var b = new FakeProvider("b", Other);
        var c = new FakeProvider("c");
        var client = new ScriptedClient(_ => RunTool("a"), _ => RunTool("b"), _ => RunTool("c"));

        var result = await Run(client, 2, a, b, c);

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
        Assert.Contains("2 allowed", result.UnresolvedReason);
        Assert.Equal(1, a.Runs);
        Assert.Equal(1, b.Runs);
        Assert.Equal(0, c.Runs);
        Assert.Equal(3, client.Requests.Count);
    }

    [Fact]
    public async Task May_propose_after_using_all_allowed_tool_steps()
    {
        var client = new ScriptedClient(_ => RunTool("a"), _ => Propose("c1"));

        var result = await Run(client, 1, new FakeProvider("a", Song));

        Assert.Equal(IdentificationStatus.Identified, result.Status);
    }

    [Fact]
    public async Task Unreadable_audio_is_unresolved_without_asking_the_ai()
    {
        var client = new ScriptedClient(_ => Propose("c1"));
        var identifier = new AiRecordingIdentifier("m", 3, client, new TagLibAudioMetadataReader(), []);

        var result = await identifier.IdentifyAsync(new MemoryStream([1, 2, 3]), "song.mp3", CancellationToken.None);

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
        Assert.Empty(client.Requests);
    }
}
