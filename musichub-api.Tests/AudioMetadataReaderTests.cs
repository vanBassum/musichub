using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MusicHub.Api.Metadata;

namespace MusicHub.Api.Tests;

public class AudioMetadataReaderTests
{
    private readonly IAudioMetadataReader _reader = new TagLibAudioMetadataReader();

    [Fact]
    public async Task Wav_returns_technical_properties()
    {
        using var stream = new MemoryStream(AudioSamples.Wav());

        var metadata = await _reader.ReadAsync(stream, "song.wav", CancellationToken.None);

        Assert.Equal(44100, metadata.Technical.SampleRateHz);
        Assert.Equal(2, metadata.Technical.Channels);
        Assert.Equal(16, metadata.Technical.BitDepth);
        Assert.NotNull(metadata.Technical.Duration);
        Assert.InRange(metadata.Technical.Duration!.Value.TotalSeconds, 0.9, 1.1);
        Assert.NotNull(metadata.Technical.Format);
    }

    [Fact]
    public async Task Mp3_returns_technical_properties()
    {
        using var stream = new MemoryStream(AudioSamples.Mp3());

        var metadata = await _reader.ReadAsync(stream, "song.mp3", CancellationToken.None);

        Assert.Equal(44100, metadata.Technical.SampleRateHz);
        Assert.Equal(2, metadata.Technical.Channels);
        Assert.Equal(128, metadata.Technical.BitrateKbps);
        Assert.NotNull(metadata.Technical.Duration);
    }

    [Fact]
    public async Task Tags_are_returned()
    {
        var bytes = AudioSamples.TaggedMp3(tag =>
        {
            tag.Title = "A Title";
            tag.Album = "An Album";
            tag.Track = 3;
            tag.TrackCount = 12;
            tag.Disc = 1;
            tag.DiscCount = 2;
            tag.Year = 1999;
        });
        using var stream = new MemoryStream(bytes);

        var metadata = await _reader.ReadAsync(stream, "song.mp3", CancellationToken.None);

        Assert.Equal("A Title", metadata.Tags.Title);
        Assert.Equal("An Album", metadata.Tags.Album);
        Assert.Equal(3, metadata.Tags.TrackNumber);
        Assert.Equal(12, metadata.Tags.TrackCount);
        Assert.Equal(1, metadata.Tags.DiscNumber);
        Assert.Equal(2, metadata.Tags.DiscCount);
        Assert.Equal(1999, metadata.Tags.Year);
    }

    [Fact]
    public async Task Multiple_artists_and_genres_are_preserved_in_order()
    {
        var bytes = AudioSamples.TaggedMp3(tag =>
        {
            tag.Performers = ["Second Artist", "First Artist", "Third Artist"];
            tag.Genres = ["Trip Hop", "Dream Pop"];
        });
        using var stream = new MemoryStream(bytes);

        var metadata = await _reader.ReadAsync(stream, "song.mp3", CancellationToken.None);

        Assert.Equal(["Second Artist", "First Artist", "Third Artist"], metadata.Tags.Artists);
        Assert.Equal(["Trip Hop", "Dream Pop"], metadata.Tags.Genres);
    }

    [Fact]
    public async Task Untagged_audio_gives_unknown_tags()
    {
        using var stream = new MemoryStream(AudioSamples.Wav());

        var metadata = await _reader.ReadAsync(stream, "song.wav", CancellationToken.None);

        Assert.Null(metadata.Tags.Title);
        Assert.Null(metadata.Tags.Album);
        Assert.Null(metadata.Tags.TrackNumber);
        Assert.Null(metadata.Tags.TrackCount);
        Assert.Null(metadata.Tags.DiscNumber);
        Assert.Null(metadata.Tags.DiscCount);
        Assert.Null(metadata.Tags.Year);
        Assert.Empty(metadata.Tags.Artists);
        Assert.Empty(metadata.Tags.Genres);
    }

    [Fact]
    public async Task The_stream_is_left_open()
    {
        using var stream = new MemoryStream(AudioSamples.Wav());

        await _reader.ReadAsync(stream, "song.wav", CancellationToken.None);

        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task Empty_input_is_unsupported()
    {
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<UnsupportedAudioException>(() => _reader.ReadAsync(stream, "song.mp3", CancellationToken.None));
    }

    [Theory]
    [InlineData("song.mp3")]
    [InlineData("song.wav")]
    [InlineData("song.flac")]
    [InlineData(null)]
    public async Task Random_bytes_are_unsupported(string? fileName)
    {
        var bytes = new byte[4096];
        new Random(1).NextBytes(bytes);
        using var stream = new MemoryStream(bytes);

        await Assert.ThrowsAsync<UnsupportedAudioException>(() => _reader.ReadAsync(stream, fileName, CancellationToken.None));
    }

    [Fact]
    public async Task Truncated_input_is_unsupported()
    {
        using var stream = new MemoryStream(AudioSamples.Wav()[..20]);

        await Assert.ThrowsAsync<UnsupportedAudioException>(() => _reader.ReadAsync(stream, "song.wav", CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_is_not_wrapped()
    {
        using var stream = new MemoryStream(AudioSamples.Wav());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _reader.ReadAsync(stream, "song.wav", cts.Token));
    }

    [Fact]
    public async Task The_reader_can_be_replaced_with_a_fake()
    {
        var fake = new FakeReader();
        var services = new ServiceCollection();
        services.AddAudioMetadata();
        Assert.IsType<TagLibAudioMetadataReader>(services.BuildServiceProvider().GetRequiredService<IAudioMetadataReader>());

        services.Replace(ServiceDescriptor.Singleton<IAudioMetadataReader>(fake));
        var reader = services.BuildServiceProvider().GetRequiredService<IAudioMetadataReader>();
        using var stream = new MemoryStream();
        var metadata = await reader.ReadAsync(stream, null, CancellationToken.None);

        Assert.Same(fake, reader);
        Assert.Equal("Fake", metadata.Tags.Title);
    }

    private sealed class FakeReader : IAudioMetadataReader
    {
        public Task<AudioMetadata> ReadAsync(Stream audio, string? fileNameHint, CancellationToken ct) =>
            Task.FromResult(new AudioMetadata(
                new AudioTechnicalProperties(null, null, null, null, null, null, null),
                new AudioTags("Fake", [], null, null, null, null, null, null, [])));
    }
}
