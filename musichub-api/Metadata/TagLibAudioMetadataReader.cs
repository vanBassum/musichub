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
            if (file is TagLib.Mpeg.AudioFile && !HasConsecutiveMpegFrames(audio))
            {
                throw new UnsupportedAudioException("The input contains no valid MPEG audio frames.");
            }

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

    private static bool HasConsecutiveMpegFrames(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return true;
        }

        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            var length = (int)Math.Min(stream.Length, 1024 * 1024);
            var bytes = new byte[length];
            var read = 0;
            while (read < length)
            {
                var count = stream.Read(bytes, read, length - read);
                if (count == 0) break;
                read += count;
            }

            for (var i = 0; i + 8 <= read; i++)
            {
                if (bytes[i] != 0xFF || (bytes[i + 1] & 0xE0) != 0xE0) continue;
                var version = (bytes[i + 1] >> 3) & 3;
                var layer = (bytes[i + 1] >> 1) & 3;
                var bitrateIndex = (bytes[i + 2] >> 4) & 15;
                var sampleIndex = (bytes[i + 2] >> 2) & 3;
                if (version != 3 || layer != 1 || bitrateIndex is 0 or 15 || sampleIndex == 3) continue;
                int[] rates = [44100, 48000, 32000];
                int[] bitrates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
                var frameLength = 144 * bitrates[bitrateIndex] * 1000 / rates[sampleIndex] + ((bytes[i + 2] >> 1) & 1);
                var next = i + frameLength;
                if (next + 4 <= read && bytes[next] == 0xFF && (bytes[next + 1] & 0xFE) == (bytes[i + 1] & 0xFE)
                    && (bytes[next + 2] & 0xFC) == (bytes[i + 2] & 0xFC))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            stream.Position = originalPosition;
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

    /// <summary>
    /// Presents the stream to TagLib# read-only. Only the extension of the hint is used, to pick the format;
    /// the caller keeps ownership of the stream.
    /// </summary>
    private sealed class ReadOnlyStreamAbstraction(Stream stream, string? fileNameHint) : TagLib.File.IFileAbstraction
    {
        public string Name { get; } = "audio" + SafeExtension(fileNameHint);

        public Stream ReadStream => stream;

        public Stream WriteStream => throw new NotSupportedException("The audio is read-only.");

        public void CloseStream(Stream stream)
        {
            // The caller owns the stream.
        }

        private static string SafeExtension(string? fileNameHint)
        {
            if (string.IsNullOrWhiteSpace(fileNameHint))
            {
                return string.Empty;
            }

            var extension = Path.GetExtension(fileNameHint.AsSpan()).ToString();
            return extension.Length <= 10 && extension.All(c => char.IsAsciiLetterOrDigit(c) || c == '.') ? extension : string.Empty;
        }
    }
}
