namespace MusicHub.Api.Storage;

/// <summary>Binds the <c>FileStorage</c> configuration section (environment variables <c>FileStorage__*</c>).</summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public string? Endpoint { get; set; }
    public string? Bucket { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }

    /// <summary>Throws when a key is missing or invalid. Messages name the key and never contain values of secrets.</summary>
    public void Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            problems.Add("FileStorage__Endpoint is required.");
        }
        else if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri)
                 || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            problems.Add("FileStorage__Endpoint must be an absolute http(s) URL.");
        }

        if (string.IsNullOrWhiteSpace(Bucket))
        {
            problems.Add("FileStorage__Bucket is required.");
        }

        if (string.IsNullOrWhiteSpace(AccessKey))
        {
            problems.Add("FileStorage__AccessKey is required.");
        }

        if (string.IsNullOrWhiteSpace(SecretKey))
        {
            problems.Add("FileStorage__SecretKey is required.");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException("File storage is not configured: " + string.Join(" ", problems));
        }
    }
}
