namespace MusicHub.Api.Metadata;

/// <summary>
/// What an audio file says about itself. Tags are untrusted, file-provided evidence, not MusicHub's recording identity.
/// </summary>
/// <param name="Technical">Properties of the audio stream itself.</param>
/// <param name="Tags">Embedded tags, as written in the file.</param>
public sealed record AudioMetadata(AudioTechnicalProperties Technical, AudioTags Tags);

/// <summary>
/// Technical properties of the audio. A property that can't be determined is null, never a default.
/// </summary>
public sealed record AudioTechnicalProperties(
    string? Format,
    string? Codec,
    TimeSpan? Duration,
    int? BitrateKbps,
    int? SampleRateHz,
    int? Channels,
    int? BitDepth);

/// <summary>
/// Embedded tags. Tags are untrusted, file-provided evidence, not MusicHub's recording identity.
/// Unknown values are null, or an empty list for <see cref="Artists"/> and <see cref="Genres"/>.
/// </summary>
public sealed record AudioTags(
    string? Title,
    IReadOnlyList<string> Artists,
    string? Album,
    int? TrackNumber,
    int? TrackCount,
    int? DiscNumber,
    int? DiscCount,
    int? Year,
    IReadOnlyList<string> Genres);
