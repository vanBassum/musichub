using TagLib;

namespace MusicHub.Api.Metadata;

/// <summary><see cref="IEmbeddedCoverReader"/> on top of TagLib#.</summary>
public sealed class TagLibEmbeddedCoverReader : IEmbeddedCoverReader
{
    /// <summary>The largest embedded image that is served: 5 MiB.</summary>
    public const int MaxCoverBytes = 5 * 1024 * 1024;

    public EmbeddedCover? Read(Stream audio, string? fileNameHint)
    {
        ArgumentNullException.ThrowIfNull(audio);

        IPicture[] pictures;
        try
        {
            using var file = TagLib.File.Create(new ReadOnlyStreamAbstraction(audio, fileNameHint), ReadStyle.None);
            pictures = file.Tag?.Pictures ?? [];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Corrupt or unsupported audio has no cover to offer.
            return null;
        }

        var picture = pictures.FirstOrDefault(p => p.Type == PictureType.FrontCover) ?? pictures.FirstOrDefault();
        var data = picture?.Data.Data;
        if (data is not { Length: > 0 and <= MaxCoverBytes })
        {
            return null;
        }

        var contentType = ImageContentType(data);
        return contentType is null ? null : new EmbeddedCover(data, contentType);
    }

    // The tag's own MIME type is untrusted text: only the magic bytes of a known raster format decide.
    private static string? ImageContentType(byte[] data) => data switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [0x47, 0x49, 0x46, 0x38, ..] => "image/gif",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };
}
