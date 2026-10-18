using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Options;

namespace MusicHub.Api.Storage;

internal sealed class S3FileStorage(IAmazonS3 s3, IOptions<FileStorageOptions> options) : IFileStorage
{
    private readonly string _bucket = options.Value.Bucket!;

    public async Task PutAsync(string key, Stream content, string? contentType, CancellationToken cancellationToken)
    {
        // TransferUtility handles both seekable and non-seekable streams via multipart upload.
        using var transfer = new TransferUtility(s3);
        var request = new TransferUtilityUploadRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,
        };
        if (contentType is not null)
            request.ContentType = contentType;
        await transfer.UploadAsync(request, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var response = await s3.GetObjectAsync(_bucket, key, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            throw new FileNotFoundException($"No stored file under key '{key}'.", key, ex);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await s3.GetObjectMetadataAsync(_bucket, key, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            return false;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        // S3 answers a delete of a missing key with success, which is the idempotent contract.
        await s3.DeleteObjectAsync(_bucket, key, cancellationToken);
    }

    private static bool IsNotFound(AmazonS3Exception ex) =>
        ex.StatusCode == HttpStatusCode.NotFound || ex.ErrorCode == "NoSuchKey";
}
