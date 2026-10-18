namespace MusicHub.Api.Metadata;

/// <summary>
/// The input is empty, corrupt or not a supported audio format.
/// </summary>
public sealed class UnsupportedAudioException : Exception
{
    public UnsupportedAudioException(string message)
        : base(message)
    {
    }

    public UnsupportedAudioException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
