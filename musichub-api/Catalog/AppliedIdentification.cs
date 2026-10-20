using System.Text.Json.Serialization;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Catalog;

/// <summary>Who applied an identity to a file.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IdentificationSource
{
    /// <summary>The identification pipeline decided it.</summary>
    Automatic,

    /// <summary>A person confirmed or corrected it; automation never replaces it.</summary>
    Manual,
}

/// <summary>
/// A permanent record that an identity was applied to a file: the file was linked to a track, by the pipeline or by a
/// person. It is separate from the attempts (<see cref="IdentificationOutcome"/>), which also record the ones that
/// applied nothing. It is written in the same transaction as the change of <see cref="FileRecord.TrackId"/>, which stays
/// the file's current link.
/// </summary>
public class AppliedIdentification
{
    public int Id { get; set; }

    public int FileId { get; set; }
    public FileRecord? File { get; set; }

    /// <summary>The track the file was linked to; null once that track was deleted.</summary>
    public int? TrackId { get; set; }
    public Track? Track { get; set; }

    public IdentificationSource Source { get; set; }

    /// <summary>The attempt that led to this decision; null for a manual decision.</summary>
    public int? AttemptId { get; set; }
    public IdentificationOutcome? Attempt { get; set; }

    public DateTimeOffset AppliedAt { get; set; }
}
