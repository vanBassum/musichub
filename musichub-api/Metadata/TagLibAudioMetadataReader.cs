using TagLib;

namespace MusicHub.Api.Metadata;

/// <summary>
/// <see cref="IAudioMetadataReader"/> on top of TagLib#. Reads only the given stream.
/// </summary>
public sealed class TagLibAudioMetadataReader : IAudioMetadataReader
{
    public Task<AudioMetadata> ReadAsync(Stream audio, string? fileNameHint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ct.ThrowIfCancellationRequested();

        if (audio.CanSeek && audio.Length == 0)
        {
            throw new UnsupportedAudioException("The audio is empty.");
        }

        try
        {
            using var file = TagLib.File.Create(new ReadOnlyStreamAbstraction(audio, fileNameHint), ReadStyle.Average);
            var technical = ReadTechnical(file);
            if (technical.SampleRateHz is null || technical.Channels is null || technical.Duration is null)
            {
                throw new UnsupportedAudioException("The input contains no valid audio stream.");
            }

            return Task.FromResult(new AudioMetadata(technical, ReadTags(file.Tag)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not UnsupportedAudioException)
        {
            throw new UnsupportedAudioException("The audio is corrupt or not a supported format.", ex);
        }
    }

    private static AudioTechnicalProperties ReadTechnical(TagLib.File file)
    {
        var properties = file.Properties;
        var mime = file.MimeType;
        var format = mime is null ? null : mime[(mime.IndexOf('/') + 1)..];

        return new AudioTechnicalProperties(
            Format: NullIfEmpty(format),
            Codec: NullIfEmpty(properties?.Codecs.FirstOrDefault()?.Description),
            Duration: properties is { Duration.Ticks: > 0 } ? properties.Duration : null,
            BitrateKbps: PositiveOrNull(properties?.AudioBitrate),
            SampleRateHz: PositiveOrNull(properties?.AudioSampleRate),
            Channels: PositiveOrNull(properties?.AudioChannels),
            BitDepth: PositiveOrNull(properties?.BitsPerSample));
    }

    private static AudioTags ReadTags(Tag? tag)
    {
        if (tag is null)
        {
            return new AudioTags(null, [], null, null, null, null, null, null, []);
        }

        return new AudioTags(
            Title: NullIfEmpty(tag.Title),
            Artists: Values(tag.Performers),
            Album: NullIfEmpty(tag.Album),
            TrackNumber: PositiveOrNull(tag.Track),
            TrackCount: PositiveOrNull(tag.TrackCount),
            DiscNumber: PositiveOrNull(tag.Disc),
            DiscCount: PositiveOrNull(tag.DiscCount),
            Year: PositiveOrNull(tag.Year),
            Genres: Values(tag.Genres));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int? PositiveOrNull(int? value) => value is > 0 ? value : null;

    private static int? PositiveOrNull(uint value) => value is > 0 and <= int.MaxValue ? (int)value : null;

    private static string[] Values(string[]? values) =>
        values?.Where(v => !string.IsNullOrWhiteSpace(v)).ToArray() ?? [];
}
