namespace MusicHub.Api.Metadata;

/// <summary>
/// Presents the stream to TagLib# read-only. Only the extension of the hint is used, to pick the format;
/// the caller keeps ownership of the stream.
/// </summary>
internal sealed class ReadOnlyStreamAbstraction(Stream stream, string? fileNameHint) : TagLib.File.IFileAbstraction
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
