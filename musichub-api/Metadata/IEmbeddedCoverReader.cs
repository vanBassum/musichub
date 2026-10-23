namespace MusicHub.Api.Metadata;

/// <summary>An image embedded in an audio file. <see cref="ContentType"/> comes from the image bytes, not from the tag.</summary>
public sealed record EmbeddedCover(byte[] Data, string ContentType);

/// <summary>Reads the cover art an audio file carries in its own tags.</summary>
public interface IEmbeddedCoverReader
{
    /// <summary>
    /// The front cover of <paramref name="audio"/> (the first picture when none is marked as the front), or null when
    /// the file has none, it isn't a JPEG, PNG, GIF or WebP image, it exceeds <see cref="TagLibEmbeddedCoverReader.MaxCoverBytes"/>,
    /// or the audio can't be read. Reads only the given seekable stream; the caller keeps ownership of it.
    /// </summary>
    EmbeddedCover? Read(Stream audio, string? fileNameHint);
}
