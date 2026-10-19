namespace MusicHub.Api.Catalog;

/// <summary>
/// An external track the user wants whose identity could not be established: catalog tracks that might be the same
/// recording exist, but nothing proves it. It is not a catalog track and is never merged automatically; it waits for
/// an identity decision. One case per provider recording.
/// </summary>
public class IdentityReview
{
    public int Id { get; set; }
    public required string Provider { get; set; }
    public required string ExternalId { get; set; }
    public required string Title { get; set; }
    public string? Disambiguation { get; set; }

    /// <summary>The artist names as the provider gave them, as a JSON array.</summary>
    public required string ArtistsJson { get; set; }

    /// <summary>The catalog tracks that resembled the external track when the case was opened, as a JSON array of ids.</summary>
    public required string CandidateTrackIdsJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
