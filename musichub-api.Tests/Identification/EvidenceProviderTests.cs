using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MusicHub.Api.Identification;
using MusicHub.Api.Metadata;

namespace MusicHub.Api.Tests.Identification;

public class EvidenceProviderTests
{
    private sealed class FakeSearch(params EvidenceCandidate[] candidates) : ICandidateSearch
    {
        public string Source => "fake-search";

        public Task<IReadOnlyList<EvidenceCandidate>> SearchAsync(
            string title, IReadOnlyList<string> artists, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<EvidenceCandidate>>(candidates);
    }

    private sealed class FakeFingerprint(params EvidenceCandidate[] candidates) : IAudioFingerprintMatcher
    {
        public string Source => "fake-fingerprint";

        public Task<IReadOnlyList<EvidenceCandidate>> MatchAsync(
            Stream audio, string? fileNameHint, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<EvidenceCandidate>>(candidates);
    }

    private static IRecordingIdentifier Resolve(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddAudioMetadata();
        services.AddIdentification(new ConfigurationBuilder().Build());
        configure?.Invoke(services);
        return services.BuildServiceProvider().GetRequiredService<IRecordingIdentifier>();
    }

    private static MemoryStream TaggedStream(string title, string artist, string? album = null) =>
        new(AudioSamples.TaggedMp3(tag =>
        {
            tag.Title = title;
            tag.Performers = [artist];
            tag.Album = album;
        }));

    private static EvidenceCandidate Candidate(string title, string artist, string? album = null) =>
        new(title, [artist], album);

    [Fact]
    public async Task Unavailable_tools_are_reported_explicitly_not_faked()
    {
        using var stream = TaggedStream("A Title", "An Artist");

        var result = await Resolve().IdentifyAsync(stream, "song.mp3", CancellationToken.None);

        Assert.Equal(IdentificationStatus.Identified, result.Status);
        Assert.Contains(result.Evidence, e => e.Source == "candidate-search" && e.Description.StartsWith("Unavailable"));
        Assert.Contains(result.Evidence, e => e.Source == "audio-fingerprint" && e.Description.StartsWith("Unavailable"));
    }

    [Fact]
    public async Task Agreeing_sources_identify_and_keep_their_evidence_apart()
    {
        using var stream = TaggedStream("A Title", "An Artist");

        var result = await Resolve(s =>
        {
            s.AddSingleton<ICandidateSearch>(new FakeSearch(Candidate("a title", "AN ARTIST", "The Single")));
            s.AddSingleton<IAudioFingerprintMatcher>(new FakeFingerprint(Candidate("A Title", "An Artist")));
        }).IdentifyAsync(stream, "song.mp3", CancellationToken.None);

        Assert.Equal(IdentificationStatus.Identified, result.Status);
        Assert.Equal("A Title", result.Identity!.Title);
        Assert.Equal("The Single", result.Identity.Album);
        Assert.Contains(result.Evidence, e => e.Source == "file-metadata");
        Assert.Contains(result.Evidence, e => e.Source == "fake-search");
        Assert.Contains(result.Evidence, e => e.Source == "fake-fingerprint");
    }

    [Fact]
    public async Task Conflicting_sources_leave_the_file_unresolved_with_all_evidence()
    {
        using var stream = TaggedStream("A Title", "An Artist");

        var result = await Resolve(s =>
            s.AddSingleton<IAudioFingerprintMatcher>(new FakeFingerprint(Candidate("Other Song", "Other Artist"))))
            .IdentifyAsync(stream, "song.mp3", CancellationToken.None);

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
        Assert.Null(result.Identity);
        Assert.Contains("2 different recordings", result.UnresolvedReason);
        Assert.Contains(result.Evidence, e => e.Source == "file-metadata" && e.Description.Contains("A Title"));
        Assert.Contains(result.Evidence, e => e.Source == "fake-fingerprint" && e.Description.Contains("Other Song"));
    }

    [Fact]
    public async Task One_source_with_several_different_candidates_is_unresolved()
    {
        using var stream = TaggedStream("A Title", "An Artist");

        var result = await Resolve(s =>
            s.AddSingleton<ICandidateSearch>(new FakeSearch(
                Candidate("A Title", "An Artist"), Candidate("A Title (Live)", "An Artist"))))
            .IdentifyAsync(stream, "song.mp3", CancellationToken.None);

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
        Assert.Null(result.Identity);
    }

    [Fact]
    public async Task A_different_artist_for_the_same_title_is_a_conflict()
    {
        using var stream = TaggedStream("A Title", "An Artist");

        var result = await Resolve(s =>
            s.AddSingleton<ICandidateSearch>(new FakeSearch(Candidate("A Title", "Someone Else"))))
            .IdentifyAsync(stream, "song.mp3", CancellationToken.None);

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
    }

    [Fact]
    public async Task No_candidate_from_any_tool_is_unresolved()
    {
        using var stream = new MemoryStream(AudioSamples.Mp3());

        var result = await Resolve().IdentifyAsync(stream, "song.mp3", CancellationToken.None);

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
        Assert.Equal(3, result.Evidence.Count);
    }

    [Fact]
    public async Task A_fingerprint_without_tags_identifies_when_it_is_the_only_candidate()
    {
        using var stream = new MemoryStream(AudioSamples.Mp3());

        var result = await Resolve(s =>
            s.AddSingleton<IAudioFingerprintMatcher>(new FakeFingerprint(Candidate("A Title", "An Artist"))))
            .IdentifyAsync(stream, "song.mp3", CancellationToken.None);

        Assert.Equal(IdentificationStatus.Identified, result.Status);
        Assert.Equal("A Title", result.Identity!.Title);
    }

    [Fact]
    public async Task A_search_without_a_match_is_reported_as_found_nothing()
    {
        using var stream = TaggedStream("A Title", "An Artist");

        var result = await Resolve(s => s.AddSingleton<ICandidateSearch>(new FakeSearch()))
            .IdentifyAsync(stream, "song.mp3", CancellationToken.None);

        Assert.Equal(IdentificationStatus.Identified, result.Status);
        Assert.Contains(result.Evidence, e => e.Source == "fake-search" && e.Description.StartsWith("Found nothing"));
    }
}
