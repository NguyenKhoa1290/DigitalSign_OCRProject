using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using SignService.Core.Interfaces;

namespace SignService.Infrastructure.Services;

public class MinioService : IMinioService
{
    private readonly IMinioClient _minioClient;
    private readonly string _bucket;
    private readonly ILogger<MinioService> _logger;

    public MinioService(IMinioClient minioClient, IConfiguration config, ILogger<MinioService> logger)
    {
        _minioClient = minioClient;
        _bucket = config["MinioSettings:Bucket"] ?? "documents";
        _logger = logger;
    }

    public async Task<byte[]> DownloadFileAsync(string objectPath)
    {
        _logger.LogInformation("Downloading file from MinIO: {Path}", objectPath);

        using var ms = new MemoryStream();

        var getArgs = new GetObjectArgs()
            .WithBucket(_bucket)
            .WithObject(objectPath)
            .WithCallbackStream(async (stream, ct) =>
            {
                await stream.CopyToAsync(ms, ct);
            });

        await _minioClient.GetObjectAsync(getArgs);

        return ms.ToArray();
    }

    public async Task UploadFileAsync(string objectPath, byte[] content, string contentType = "application/pdf")
    {
        _logger.LogInformation("Uploading file to MinIO: {Path} ({Size} bytes)", objectPath, content.Length);

        using var ms = new MemoryStream(content);

        // Ensure bucket exists
        var bucketExistsArgs = new BucketExistsArgs().WithBucket(_bucket);
        bool exists = await _minioClient.BucketExistsAsync(bucketExistsArgs);
        if (!exists)
        {
            var makeBucketArgs = new MakeBucketArgs().WithBucket(_bucket);
            await _minioClient.MakeBucketAsync(makeBucketArgs);
        }

        var putArgs = new PutObjectArgs()
            .WithBucket(_bucket)
            .WithObject(objectPath)
            .WithStreamData(ms)
            .WithObjectSize(content.Length)
            .WithContentType(contentType);

        await _minioClient.PutObjectAsync(putArgs);

        _logger.LogInformation("File uploaded successfully: {Path}", objectPath);
    }
}
