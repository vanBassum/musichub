using System.Security.Cryptography;

namespace MusicHub.Api.Acquisition;

/// <summary>The two hashes kept for every downloaded file. <see cref="AudioSha256"/> is null when there is no audio payload to hash.</summary>
public sealed record FileHashes(long SizeBytes, string FileSha256, string? AudioSha256, string AudioHashAlgorithm);

/// <summary>
/// Hashes a downloaded file twice: the whole file, and its MP3 audio payload without tags, so a file whose tags changed
/// is still recognised. Equal hashes mean equal bytes (or equal audio): never that two files are the same recording.
/// <para>
/// Algorithm <see cref="Mp3PayloadAlgorithm"/>: the payload is every byte from the end of the leading ID3v2 tag(s) to the
/// start of the trailing tags. Leading: while the bytes begin with <c>ID3</c> and a valid synchsafe size, skip the
/// 10-byte header, the size, and the 10-byte footer when the header's footer flag is set. Trailing: an ID3v1 tag
/// (the last 128 bytes, beginning <c>TAG</c>) is removed first, then an APEv2 tag (its 32-byte footer
/// <c>APETAGEX</c>, its size, and the 32-byte header when the footer's flags say there is one). No frame is parsed or
/// rewritten. The hash is SHA-256, lowercase hex. A change to any rule needs a new algorithm name.
/// </para>
/// </summary>
public static class AudioHashing
{
    public const string Mp3PayloadAlgorithm = "mp3-payload-sha256-v1";

    private const int Id3v2HeaderSize = 10;
    private const int Id3v1Size = 128;
    private const int ApeFooterSize = 32;

    /// <summary>Reads <paramref name="file"/> (seekable) from the start; leaves it open, positioned anywhere.</summary>
    public static async Task<FileHashes> ComputeAsync(Stream file, CancellationToken cancellationToken)
    {
        var fileHash = await HashRangeAsync(file, 0, file.Length, cancellationToken);
        var (start, end) = await PayloadRangeAsync(file, cancellationToken);
        var audioHash = end > start ? await HashRangeAsync(file, start, end - start, cancellationToken) : null;
        return new FileHashes(file.Length, fileHash, audioHash, Mp3PayloadAlgorithm);
    }

    /// <summary>The payload's byte range: start inclusive, end exclusive. Empty (start ≥ end) when the file is all tag.</summary>
    public static async Task<(long Start, long End)> PayloadRangeAsync(Stream file, CancellationToken cancellationToken)
    {
        long start = 0, end = file.Length;
        while (await ReadAtAsync(file, start, Id3v2HeaderSize, cancellationToken) is { } header && IsId3v2(header))
        {
            var footer = (header[5] & 0x10) != 0 ? Id3v2HeaderSize : 0;
            start += Id3v2HeaderSize + SynchsafeSize(header) + footer;
        }

        if (end - Id3v1Size >= start
            && await ReadAtAsync(file, end - Id3v1Size, 3, cancellationToken) is { } v1
            && v1.AsSpan().SequenceEqual("TAG"u8))
        {
            end -= Id3v1Size;
        }

        if (end - ApeFooterSize >= start
            && await ReadAtAsync(file, end - ApeFooterSize, ApeFooterSize, cancellationToken) is { } ape
            && ape.AsSpan(0, 8).SequenceEqual("APETAGEX"u8))
        {
            // The footer's size counts the items and the footer; bit 31 of the flags says a header precedes them.
            var size = BitConverter.ToUInt32(ape, 12);
            var hasHeader = (BitConverter.ToUInt32(ape, 20) & 0x80000000) != 0;
            end -= size + (hasHeader ? ApeFooterSize : 0);
        }

        return (start, end);
    }

    private static bool IsId3v2(byte[] header) =>
        header.AsSpan(0, 3).SequenceEqual("ID3"u8) && header[6] < 0x80 && header[7] < 0x80 && header[8] < 0x80 && header[9] < 0x80;

    private static int SynchsafeSize(byte[] header) =>
        (header[6] << 21) | (header[7] << 14) | (header[8] << 7) | header[9];

    // Null when fewer than `count` bytes are there.
    private static async Task<byte[]?> ReadAtAsync(Stream file, long position, int count, CancellationToken cancellationToken)
    {
        if (position < 0 || position + count > file.Length)
            return null;
        file.Position = position;
        var buffer = new byte[count];
        await file.ReadExactlyAsync(buffer, cancellationToken);
        return buffer;
    }

    private static async Task<string> HashRangeAsync(Stream file, long start, long length, CancellationToken cancellationToken)
    {
        file.Position = start;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        for (var left = length; left > 0;)
        {
            var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), cancellationToken);
            if (read == 0)
                break;
            sha.AppendData(buffer, 0, read);
            left -= read;
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}
