using System.Buffers;
using System.Security.Cryptography;

namespace MusicHub.Api.Acquisition;

/// <summary>How the audio-payload hash of a file came out. Only <see cref="Computed"/> carries a hash.</summary>
public enum AudioHashStatus
{
    /// <summary>The whole file parsed as MP3 and the hash was calculated.</summary>
    Computed,

    /// <summary>A well-formed file this version does not hash: not Layer III, free-format bitrate, or no ID3/MPEG framing it knows.</summary>
    Unsupported,

    /// <summary>The bytes claim to be MP3 but do not parse cleanly (broken header, truncated frame, unknown trailing data).</summary>
    Malformed,
}

/// <param name="Hash">Lowercase hex SHA-256 of the audio payload; null unless <paramref name="Status"/> is Computed.</param>
public sealed record AudioHashResult(AudioHashStatus Status, string? Hash);

/// <summary>
/// The metadata-independent hash of an MP3's encoded audio (algorithm <see cref="Version"/> 1). It recognises the same
/// encoded audio under different tags; it is not the full-file hash and not an acoustic fingerprint, and equal hashes
/// do not prove which recording a file is.
/// <para>
/// Extraction is strict and deterministic. Read from the start:
/// 1. Skip every leading ID3v2 tag (10-byte header, syncsafe size, plus a 10-byte footer when flagged).
/// 2. Read consecutive MPEG audio Layer III frames, MPEG-1, 2 and 2.5. A frame is its 4-byte header, then the rest of
///    its length: 144000 * bitrate / sampleRate + padding bytes (MPEG-1) or 72000 * ... (MPEG-2/2.5). Its bytes are
///    taken exactly as stored: a Xing/Info frame counts as a frame, and there is no resynchronisation.
/// 3. After the last frame, the file must end or continue with a known non-audio trailer (ID3v1 "TAG", "APETAGEX",
///    "LYRICSBEGIN"); that trailer and everything after it is excluded. Any other bytes, an invalid header, or a
///    truncated frame make the file <see cref="AudioHashStatus.Malformed"/>; no hash is produced.
/// 4. The hash is SHA-256 over the concatenated frame bytes. Layer I/II or free-format frames make it
///    <see cref="AudioHashStatus.Unsupported"/>.
/// </para>
/// Changing any rule above changes the hash, so it requires a new <see cref="Version"/>; stored hashes keep the version
/// that produced them.
/// </summary>
public static class Mp3AudioPayload
{
    public const int Version = 1;

    private static readonly int[] Mpeg1Bitrates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] Mpeg2Bitrates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
    private static readonly int[][] SampleRates = [[11025, 12000, 8000], [], [22050, 24000, 16000], [44100, 48000, 32000]];

    private static readonly AudioHashResult MalformedResult = new(AudioHashStatus.Malformed, null);
    private static readonly AudioHashResult UnsupportedResult = new(AudioHashStatus.Unsupported, null);

    /// <summary>Reads <paramref name="content"/> to its end or until it is clear no hash can result.</summary>
    public static async Task<AudioHashResult> HashAsync(Stream content, CancellationToken ct)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var header = new byte[10];
        var frames = 0;

        while (true)
        {
            var read = await ReadFullyAsync(content, header.AsMemory(0, 4), ct);
            if (read == 0 && frames > 0) return Computed(hasher);
            if (read < 4) return MalformedResult;

            if (frames == 0 && header[0] == 'I' && header[1] == 'D' && header[2] == '3')
            {
                if (!await SkipId3V2Async(content, header, ct)) return MalformedResult;
                continue;
            }

            if (header[0] != 0xFF || (header[1] & 0xE0) != 0xE0)
            {
                return frames > 0 && IsTrailer(header) ? Computed(hasher) : MalformedResult;
            }

            if (FrameLength(header) is not { } length) return MalformedResult;
            if (length == 0) return UnsupportedResult;

            var frame = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                header.AsSpan(0, 4).CopyTo(frame);
                if (await ReadFullyAsync(content, frame.AsMemory(4, length - 4), ct) < length - 4) return MalformedResult;
                hasher.AppendData(frame.AsSpan(0, length));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(frame);
            }
            frames++;
        }
    }

    private static AudioHashResult Computed(IncrementalHash hasher) =>
        new(AudioHashStatus.Computed, Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant());

    private static bool IsTrailer(byte[] four) =>
        (four[0] == 'T' && four[1] == 'A' && four[2] == 'G')
        || (four[0] == 'A' && four[1] == 'P' && four[2] == 'E' && four[3] == 'T')
        || (four[0] == 'L' && four[1] == 'Y' && four[2] == 'R' && four[3] == 'I');

    /// <summary>The frame's byte length; 0 when valid but not hashed by this version; null when the header is invalid.</summary>
    private static int? FrameLength(byte[] header)
    {
        var version = (header[1] >> 3) & 3;
        var layer = (header[1] >> 1) & 3;
        if (version == 1 || layer == 0) return null;
        if (layer != 1) return 0;

        var bitrateIndex = header[2] >> 4;
        var sampleRateIndex = (header[2] >> 2) & 3;
        if (bitrateIndex == 15 || sampleRateIndex == 3) return null;
        if (bitrateIndex == 0) return 0;

        var mpeg1 = version == 3;
        var bitrate = (mpeg1 ? Mpeg1Bitrates : Mpeg2Bitrates)[bitrateIndex] * 1000;
        var padding = (header[2] >> 1) & 1;
        return (mpeg1 ? 144 : 72) * bitrate / SampleRates[version][sampleRateIndex] + padding;
    }

    /// <summary>Skips an ID3v2 tag whose first four bytes are in <paramref name="header"/>; false when it is broken or cut off.</summary>
    private static async Task<bool> SkipId3V2Async(Stream content, byte[] header, CancellationToken ct)
    {
        if (await ReadFullyAsync(content, header.AsMemory(4, 6), ct) < 6) return false;
        if (header[3] is < 2 or > 4) return false;
        if ((header[6] | header[7] | header[8] | header[9]) >= 0x80) return false;

        long size = (header[6] << 21) | (header[7] << 14) | (header[8] << 7) | header[9];
        if ((header[5] & 0x10) != 0) size += 10;

        var scratch = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (size > 0)
            {
                var chunk = (int)Math.Min(size, scratch.Length);
                var read = await ReadFullyAsync(content, scratch.AsMemory(0, chunk), ct);
                if (read < chunk) return false;
                size -= chunk;
            }
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    private static async Task<int> ReadFullyAsync(Stream content, Memory<byte> buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await content.ReadAsync(buffer[total..], ct);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
