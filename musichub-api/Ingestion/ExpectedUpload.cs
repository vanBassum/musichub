namespace MusicHub.Api.Ingestion;

/// <summary>
/// What the client declared before sending the bytes: the exact byte length and the lowercase hex
/// SHA-256. Request-local: it is checked against the received bytes and never persisted.
/// </summary>
public sealed record ExpectedUpload(long Length, string Sha256Hex);
