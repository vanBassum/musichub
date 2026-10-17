using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;

namespace MusicHub.Api.Storage;

public static class FileStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFileStorage"/> on an S3-compatible store. The configuration is validated
    /// when the client or the storage is first resolved, not at startup, and nothing connects until first use.
    /// </summary>
    public static IServiceCollection AddFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));

        services.AddSingleton<IAmazonS3>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<FileStorageOptions>>().Value;
            options.Validate();

            return new AmazonS3Client(
                new BasicAWSCredentials(options.AccessKey, options.SecretKey),
                new AmazonS3Config
                {
                    ServiceURL = options.Endpoint,
                    ForcePathStyle = true,
                });
        });

        services.AddSingleton<IFileStorage, S3FileStorage>();

        return services;
    }
}
