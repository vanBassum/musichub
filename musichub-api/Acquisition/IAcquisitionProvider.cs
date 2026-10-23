namespace MusicHub.Api.Acquisition;

/// <summary>What to look for. Providers turn it into their own query; all fields are hints, never an identity.</summary>
public record AcquisitionQuery(string Artist, string Title, string? Album = null);

/// <summary>
/// A file a provider offers, as the provider describes it. Nothing here establishes which recording the file is:
/// the name and the metadata are the source's claims, and identification decides after the download.
/// </summary>
/// <param name="Provider">The provider's name; the provenance of this candidate.</param>
/// <param name="Source">Who offers the file, as the provider names it (for example a peer).</param>
/// <param name="Path">The file's path at the source, exactly as the provider gave it.</param>
/// <param name="HasFreeSlot">Whether the source can start sending now, when the provider says.</param>
/// <param name="QueueLength">How many transfers wait at the source, when the provider says.</param>
public record AcquisitionCandidate(
    string Provider,
    string Source,
    string Path,
    long SizeBytes,
    string? Format = null,
    int? BitrateKbps = null,
    int? SampleRateHz = null,
    int? BitDepth = null,
    int? DurationSeconds = null,
    bool? HasFreeSlot = null,
    int? QueueLength = null)
{
    /// <summary>The last segment of <see cref="Path"/>, whichever separator the source uses.</summary>
    public string FileName => RemotePath.FileName(Path);
}

/// <summary>Reads a path as a source gave it, which may use either separator.</summary>
public static class RemotePath
{
    public static string[] Segments(string path) => path.Split('\\', '/');

    public static string FileName(string path) => Segments(path)[^1];
}

/// <summary>Why a download did not deliver a file; the outcome's detail says what to do about it.</summary>
public enum AcquisitionFailure
{
    /// <summary>The provider could not be reached, refused our credentials, or is not set up.</summary>
    Unavailable,

    /// <summary>The source declined the request.</summary>
    Rejected,

    /// <summary>The transfer started but the source or the network ended it.</summary>
    TransferFailed,

    /// <summary>The download did not finish within the configured bound.</summary>
    TimedOut,

    /// <summary>The provider reports it finished, but MusicHub cannot read the file.</summary>
    FileNotAccessible,

    /// <summary>The file arrived, but MusicHub failed while processing it (hashing, identifying, importing).</summary>
    ProcessingFailed,

    /// <summary>The provider confirmed the source is offline. The source, not the track, failed: it is skipped for a while.</summary>
    SourceOffline,
}

/// <summary>A downloaded file, readable by MusicHub. The provider keeps ownership of the underlying storage.</summary>
public interface IAcquiredFile
{
    string FileName { get; }

    /// <summary>Opens the bytes for reading, as a seekable stream (hashing and identification read it more than once); the caller disposes it.</summary>
    Stream OpenRead();
}

/// <summary>
/// How one download ended: either <see cref="File"/> is set, or <see cref="Failure"/> and <see cref="Detail"/> are.
/// A completed download only means bytes arrived: it says nothing about what recording they hold.
/// </summary>
public sealed record AcquisitionDownloadOutcome
{
    private AcquisitionDownloadOutcome(string provider, IAcquiredFile? file, AcquisitionFailure? failure, string? detail)
    {
        Provider = provider;
        File = file;
        Failure = failure;
        Detail = detail;
    }

    public string Provider { get; }
    public IAcquiredFile? File { get; }
    public AcquisitionFailure? Failure { get; }

    /// <summary>For a failure: what went wrong and what to check. Never holds a secret.</summary>
    public string? Detail { get; }

    public static AcquisitionDownloadOutcome Completed(string provider, IAcquiredFile file) =>
        new(provider, file, null, null);

    public static AcquisitionDownloadOutcome Failed(string provider, AcquisitionFailure failure, string detail) =>
        new(provider, null, failure, detail);
}

/// <summary>The provider could not answer (unreachable, timeout, bad credentials, server error): not "no match".</summary>
public class AcquisitionUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>The provider confirmed that the candidate's source is offline; nothing else about the provider is wrong.</summary>
public sealed class AcquisitionSourceOfflineException()
    : AcquisitionUnavailableException("The source is offline.");

/// <summary>
/// Finds and downloads music from one acquisition provider. It knows nothing about the catalog, ranking,
/// identification or import: it reports what the provider offers and what a download delivered. A caller's
/// cancellation stops the work, also at the provider, and surfaces as <see cref="OperationCanceledException"/>.
/// </summary>
public interface IAcquisitionProvider
{
    /// <summary>The provider's name; carried on every candidate and outcome as provenance.</summary>
    string Name { get; }

    /// <summary>
    /// What the provider offers for the query, in the provider's order (not a ranking). An empty list means the
    /// provider has nothing; a provider that cannot answer throws <see cref="AcquisitionUnavailableException"/>.
    /// </summary>
    Task<IReadOnlyList<AcquisitionCandidate>> SearchAsync(AcquisitionQuery query, CancellationToken ct);

    /// <summary>Downloads the candidate within the provider's bound. Failures are returned, not thrown.</summary>
    Task<AcquisitionDownloadOutcome> DownloadAsync(AcquisitionCandidate candidate, CancellationToken ct);

    /// <summary>
    /// Like <see cref="DownloadAsync"/>, for a download that may already be under way at the provider (after a restart):
    /// it follows that transfer instead of starting another, calls <paramref name="reportProgress"/> with the bytes
    /// received so far, and, when cancelled, leaves the transfer at the provider so a later call can follow it again.
    /// It never removes a transfer or a file it did not start.
    /// </summary>
    Task<AcquisitionDownloadOutcome> ResumeDownloadAsync(
        AcquisitionCandidate candidate, Func<long, Task> reportProgress, CancellationToken ct);
}
