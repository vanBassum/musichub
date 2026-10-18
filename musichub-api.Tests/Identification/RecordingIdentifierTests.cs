using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MusicHub.Api.Identification;
using MusicHub.Api.Metadata;

namespace MusicHub.Api.Tests.Identification;

public class RecordingIdentifierTests
{
    private static IRecordingIdentifier Resolve(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddAudioMetadata();
        services.AddIdentification(configuration);
        return services.BuildServiceProvider().GetRequiredService<IRecordingIdentifier>();
    }

    [Fact]
    public void Defaults_to_the_rule_based_resolver()
    {
        Assert.IsType<RuleBasedRecordingIdentifier>(Resolve([]));
    }

    [Fact]
    public void Rule_based_strategy_is_selected_by_configuration()
    {
        Assert.IsType<RuleBasedRecordingIdentifier>(Resolve(new() { ["Identification:Strategy"] = "RuleBased" }));
    }

    [Fact]
    public void Ai_strategy_is_selected_by_configuration()
    {
        var identifier = Resolve(new() { ["Identification:Strategy"] = "Ai", ["Identification:AiModel"] = "some-model" });

        var ai = Assert.IsType<AiRecordingIdentifier>(identifier);
        Assert.Equal("some-model", ai.Model);
    }

    [Fact]
    public void Ai_strategy_without_a_model_fails_instead_of_falling_back()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve(new() { ["Identification:Strategy"] = "Ai" }));

        Assert.Contains("Identification__AiModel", ex.Message);
    }

    [Fact]
    public async Task Ai_resolver_leaves_the_file_unresolved()
    {
        var identifier = Resolve(new() { ["Identification:Strategy"] = "Ai", ["Identification:AiModel"] = "some-model" });
        using var stream = new MemoryStream(AudioSamples.Mp3());

        var result = await identifier.IdentifyAsync(stream, "song.mp3", CancellationToken.None);

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
        Assert.Null(result.Identity);
        Assert.NotNull(result.UnresolvedReason);
    }

    [Fact]
    public async Task Rule_based_proposes_the_tagged_identity_with_evidence()
    {
        var bytes = AudioSamples.TaggedMp3(tag =>
        {
            tag.Title = "A Title";
            tag.Performers = ["An Artist"];
            tag.Album = "An Album";
        });
        using var stream = new MemoryStream(bytes);

        var result = await Resolve([]).IdentifyAsync(stream, "song.mp3", CancellationToken.None);

        Assert.Equal(IdentificationStatus.Identified, result.Status);
        Assert.Equal("A Title", result.Identity!.Title);
        Assert.Equal(["An Artist"], result.Identity.Artists);
        Assert.Equal("An Album", result.Identity.Album);
        Assert.Null(result.UnresolvedReason);
        Assert.Contains(result.Evidence, e => e.Source == "file-metadata");
    }

    [Fact]
    public async Task Rule_based_is_unresolved_without_title_and_artist()
    {
        using var stream = new MemoryStream(AudioSamples.TaggedMp3(tag => tag.Title = "Only A Title"));

        var result = await Resolve([]).IdentifyAsync(stream, "song.mp3", CancellationToken.None);

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
        Assert.Null(result.Identity);
        Assert.NotNull(result.UnresolvedReason);
    }

    [Fact]
    public async Task Rule_based_is_unresolved_for_unreadable_audio()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await Resolve([]).IdentifyAsync(stream, "song.mp3", CancellationToken.None);

        Assert.Equal(IdentificationStatus.Unresolved, result.Status);
        Assert.Null(result.Identity);
    }
}
