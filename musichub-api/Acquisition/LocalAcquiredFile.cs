namespace MusicHub.Api.Acquisition;

/// <summary>A downloaded file on a disk MusicHub can read. MusicHub only reads it: the provider owns the folder.</summary>
public sealed class LocalAcquiredFile(string fileName, string path) : IAcquiredFile
{
    public string FileName => fileName;

    public Stream OpenRead() =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
}
