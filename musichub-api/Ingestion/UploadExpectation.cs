namespace MusicHub.Api.Ingestion;

/// <summary>
/// What the client declared about an upload before sending its bytes: exact byte length and
/// lowercase hex SHA-256. Request-local: it is verified while streaming and never persisted.
/// </summary>
public sealed record UploadExpectation(long Length, string Sha256)
{
    /// <summary>Parses client-supplied values; null when the digest is not 64 hex characters or the length is negative.</summary>
    public static UploadExpectation? TryCreate(long length, string sha256) =>
        length >= 0 && sha256.Length == 64 && sha256.All(Uri.IsHexDigit)
            ? new UploadExpectation(length, sha256.ToLowerInvariant())
            : null;
}
