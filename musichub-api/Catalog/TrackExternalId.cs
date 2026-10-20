namespace MusicHub.Api.Catalog;

/// <summary>
/// Proof that an external provider's recording is this catalog track: the provider's own stable identifier. It is the
/// only evidence the wishlist treats as reliable, so a provider recording maps to at most one track.
/// </summary>
public class TrackExternalId
{
    public int Id { get; set; }
    public int TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public required string Provider { get; set; }
    public required string ExternalId { get; set; }
}
