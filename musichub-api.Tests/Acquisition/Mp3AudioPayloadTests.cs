using System.Security.Cryptography;
using System.Text;
using MusicHub.Api.Acquisition;

namespace MusicHub.Api.Tests.Acquisition;

public sealed class Mp3AudioPayloadTests
{
    // MPEG-1 Layer III, 128 kbps, 44.1 kHz, no padding: 417 bytes per frame.
    private const int FrameBytes = 417;

    private static byte[] Frame(byte fill)
    {
        var frame = new byte[FrameBytes];
        Array.Fill(frame, fill);
        frame[0] = 0xFF;
        frame[1] = 0xFB;
        frame[2] = 0x90;
        frame[3] = 0x00;
        return frame;
    }

    private static byte[] Audio(params byte[] fills) => fills.SelectMany(Frame).ToArray();

    private static byte[] Id3V2(int size, string text = "x")
    {
        var tag = new byte[10 + size];
        "ID3"u8.CopyTo(tag);
        tag[3] = 3;
        tag[6] = (byte)((size >> 21) & 0x7F);
        tag[7] = (byte)((size >> 14) & 0x7F);
        tag[8] = (byte)((size >> 7) & 0x7F);
        tag[9] = (byte)(size & 0x7F);
        Encoding.ASCII.GetBytes(text).CopyTo(tag, 10);
        return tag;
    }

    private static byte[] Id3V1()
    {
        var tag = new byte[128];
        "TAG"u8.CopyTo(tag);
        return tag;
    }

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static Task<AudioHashResult> Hash(byte[] bytes) => Mp3AudioPayload.HashAsync(new MemoryStream(bytes), default);

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [Fact]
    public async Task The_hash_is_the_sha256_of_the_frames()
    {
        var audio = Audio(1, 2, 3);

        var result = await Hash(audio);

        Assert.Equal(AudioHashStatus.Computed, result.Status);
        Assert.Equal(Sha256(audio), result.Hash);
    }

    [Fact]
    public async Task Tags_change_the_file_hash_but_not_the_audio_hash()
    {
        var audio = Audio(1, 2, 3);
        var bare = await Hash(audio);

        var tagged = Join(Id3V2(200, "Artist"), audio, Id3V1());
        var retagged = Join(Id3V2(40, "Other"), Id3V2(10), audio, "APETAGEX and more"u8.ToArray());

        Assert.NotEqual(Sha256(audio), Sha256(tagged));
        Assert.NotEqual(Sha256(tagged), Sha256(retagged));
        Assert.Equal(bare.Hash, (await Hash(tagged)).Hash);
        Assert.Equal(bare.Hash, (await Hash(retagged)).Hash);
    }

    [Fact]
    public async Task Different_encoded_audio_has_a_different_hash()
    {
        var a = await Hash(Join(Id3V2(20), Audio(1, 2, 3)));
        var changed = await Hash(Join(Id3V2(20), Audio(1, 9, 3)));
        var shorter = await Hash(Join(Id3V2(20), Audio(1, 2)));

        Assert.NotEqual(a.Hash, changed.Hash);
        Assert.NotEqual(a.Hash, shorter.Hash);
    }

    [Fact]
    public async Task A_truncated_final_frame_is_malformed_with_no_hash()
    {
        var result = await Hash(Audio(1, 2)[..^10]);

        Assert.Equal(AudioHashStatus.Malformed, result.Status);
        Assert.Null(result.Hash);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("noise")]
    [InlineData("tag-only")]
    [InlineData("junk-after-audio")]
    [InlineData("junk-between-frames")]
    [InlineData("cut-id3v2")]
    [InlineData("reserved-bitrate")]
    public async Task Bytes_that_do_not_parse_are_malformed_with_no_hash(string kind)
    {
        var bytes = kind switch
        {
            "empty" => [],
            "noise" => Encoding.ASCII.GetBytes("this is not an mp3 file at all"),
            "tag-only" => Join(Id3V2(30), Id3V1()),
            "junk-after-audio" => Join(Audio(1, 2), new byte[] { 0, 0, 0, 0, 0 }),
            "junk-between-frames" => Join(Audio(1), new byte[] { 0, 0, 0, 0 }, Audio(2)),
            "cut-id3v2" => Id3V2(500)[..100],
            _ => [0xFF, 0xFB, 0xF0, 0x00],
        };

        var result = await Hash(bytes);

        Assert.Equal(AudioHashStatus.Malformed, result.Status);
        Assert.Null(result.Hash);
    }

    [Fact]
    public async Task Layer_two_and_free_format_are_unsupported_not_hashed()
    {
        var layerTwo = Audio(1);
        layerTwo[1] = 0xFD;
        var freeFormat = Audio(1);
        freeFormat[2] = 0x00;

        foreach (var bytes in new[] { layerTwo, freeFormat })
        {
            var result = await Hash(bytes);
            Assert.Equal(AudioHashStatus.Unsupported, result.Status);
            Assert.Null(result.Hash);
        }
    }

    [Fact]
    public async Task Padding_and_mpeg2_frames_use_their_own_length()
    {
        // MPEG-2 Layer III, 64 kbps, 22.05 kHz: 72000 * 64 / 22050 = 208 bytes, +1 with padding.
        byte[] Mpeg2(bool padding)
        {
            var frame = new byte[padding ? 209 : 208];
            frame[0] = 0xFF;
            frame[1] = 0xF3;
            frame[2] = (byte)(0x80 | (padding ? 0x02 : 0));
            return frame;
        }

        var audio = Join(Mpeg2(false), Mpeg2(true), Mpeg2(false));

        var result = await Hash(audio);

        Assert.Equal(AudioHashStatus.Computed, result.Status);
        Assert.Equal(Sha256(audio), result.Hash);
    }

    [Fact]
    public async Task Both_hashes_are_calculated_from_one_read_of_the_file()
    {
        var audio = Audio(1, 2);
        var file = Join(Id3V2(64), audio, Id3V1());

        var hashes = await DownloadHashes.ComputeAsync(new BytesFile(file), default);

        Assert.Equal(Sha256(file), hashes.ContentHash);
        Assert.Equal(Sha256(audio), hashes.Audio.Hash);
    }

    [Fact]
    public async Task The_file_hash_covers_every_byte_even_when_the_audio_does_not_parse()
    {
        var bytes = Encoding.ASCII.GetBytes("not an mp3, and longer than any header read");

        var hashes = await DownloadHashes.ComputeAsync(new BytesFile(bytes), default);

        Assert.Equal(Sha256(bytes), hashes.ContentHash);
        Assert.Equal(AudioHashStatus.Malformed, hashes.Audio.Status);
    }

    private sealed class BytesFile(byte[] bytes) : IAcquiredFile
    {
        public string FileName => "x.mp3";
        public Stream OpenRead() => new MemoryStream(bytes);
    }
}
