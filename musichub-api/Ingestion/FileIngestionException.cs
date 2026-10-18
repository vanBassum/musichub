using Amazon.Runtime;

namespace MusicHub.Api.Ingestion;

/// <summary>
/// A handled upload/storage failure. The file record (<see cref="FileId"/>) exists and is
/// marked Failed when that could be persisted; the original error is the inner exception.
/// </summary>
public sealed class FileIngestionException(int fileId, Exception inner)
    : Exception("The file could not be stored.", inner)
{
    public int FileId { get; } = fileId;

    /// <summary>
    /// The safe sentence persisted as the status detail and shown to the client: it names the kind of
    /// storage failure (exception type, S3 error code, HTTP status) but never a message, key or credential.
    /// </summary>
    public string Detail { get; } = $"The storage backend failed ({Describe(inner)}) and the object could not be verified.";

    internal static string Describe(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is AmazonServiceException service)
                return $"{service.GetType().Name}, error code {service.ErrorCode ?? "none"}, HTTP {(int)service.StatusCode}";
        }
        return error.GetType().Name;
    }
}
