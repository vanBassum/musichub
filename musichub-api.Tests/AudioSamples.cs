using System.Text;

namespace MusicHub.Api.Tests;

/// <summary>
/// Builds small audio files in memory, so the tests need no binary fixtures.
/// </summary>
internal static class AudioSamples
{
    /// <summary>One second of silent 16-bit stereo PCM at 44.1 kHz.</summary>
    public static byte[] Wav()
    {
        const int sampleRate = 44100;
        const short channels = 2;
        const short bitsPerSample = 16;
        const int blockAlign = channels * bitsPerSample / 8;
        const int dataLength = sampleRate * blockAlign;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
        w.Write("RIFF"u8);
        w.Write(36 + dataLength);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(sampleRate * blockAlign);
        w.Write((short)blockAlign);
        w.Write(bitsPerSample);
        w.Write("data"u8);
        w.Write(dataLength);
        w.Write(new byte[dataLength]);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Twenty silent MPEG-1 Layer III frames: 128 kbps, 44.1 kHz, stereo (417 bytes each).</summary>
    public static byte[] Mp3()
    {
        const int frameLength = 417;
        var bytes = new byte[frameLength * 20];
        for (var i = 0; i < 20; i++)
        {
            bytes[i * frameLength] = 0xFF;
            bytes[i * frameLength + 1] = 0xFB;
            bytes[i * frameLength + 2] = 0x90;
            bytes[i * frameLength + 3] = 0x00;
        }

        return bytes;
    }

    /// <summary>The MP3 with an ID3v2 tag written by TagLib#.</summary>
    public static byte[] TaggedMp3(Action<TagLib.Tag> fill)
    {
        var abstraction = new MemoryFileAbstraction("song.mp3", Mp3());
        using (var file = TagLib.File.Create(abstraction))
        {
            fill(file.Tag);
            file.Save();
        }

        return abstraction.Stream.ToArray();
    }

    private sealed class MemoryFileAbstraction(string name, byte[] content) : TagLib.File.IFileAbstraction
    {
        public MemoryStream Stream { get; } = CreateStream(content);

        public string Name => name;

        public Stream ReadStream => Stream;

        public Stream WriteStream => Stream;

        public void CloseStream(Stream stream)
        {
            stream.Position = 0;
        }

        private static MemoryStream CreateStream(byte[] content)
        {
            var ms = new MemoryStream();
            ms.Write(content);
            ms.Position = 0;
            return ms;
        }
    }
}
