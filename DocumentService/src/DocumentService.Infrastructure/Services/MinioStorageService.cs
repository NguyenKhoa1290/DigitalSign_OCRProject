using DocumentService.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;

namespace DocumentService.Infrastructure.Services;

public class MinioStorageService : IFileStorageService
{
    private readonly IMinioClient _minioClient;
    private readonly ILogger<MinioStorageService> _logger;

    public MinioStorageService(IMinioClient minioClient, ILogger<MinioStorageService> logger)
    {
        _minioClient = minioClient;
        _logger = logger;
    }

    /// <summary>
    /// Upload file stream lên MinIO. Trả về tên file đã lưu dạng "{newGuid}{extension}".
    /// </summary>
    public async Task<string> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        string bucketName)
    {
        await EnsureBucketExistsAsync(bucketName);

        var extension = Path.GetExtension(fileName);
        var storedFileName = $"{Guid.NewGuid()}{extension}";

        var putArgs = new PutObjectArgs()
            .WithBucket(bucketName)
            .WithObject(storedFileName)
            .WithStreamData(fileStream)
            .WithObjectSize(fileStream.Length)
            .WithContentType(contentType);

        await _minioClient.PutObjectAsync(putArgs);

        _logger.LogInformation(
            "Đã upload file '{OriginalName}' lên MinIO bucket '{Bucket}' với tên '{StoredName}'",
            fileName, bucketName, storedFileName);

        return storedFileName;
    }

    /// <summary>
    /// Download file từ MinIO, trả về MemoryStream chứa nội dung file.
    /// </summary>
    public async Task<Stream> DownloadFileAsync(string storedFileName, string bucketName)
    {
        var memoryStream = new MemoryStream();

        var getArgs = new GetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(storedFileName)
            .WithCallbackStream(async (stream, cancellationToken) =>
            {
                await stream.CopyToAsync(memoryStream, cancellationToken);
            });

        await _minioClient.GetObjectAsync(getArgs);

        memoryStream.Position = 0;

        _logger.LogInformation(
            "Đã download file '{StoredName}' từ MinIO bucket '{Bucket}'",
            storedFileName, bucketName);

        return memoryStream;
    }

    /// <summary>
    /// Xóa object trên MinIO.
    /// </summary>
    public async Task<bool> DeleteFileAsync(string storedFileName, string bucketName)
    {
        try
        {
            var removeArgs = new RemoveObjectArgs()
                .WithBucket(bucketName)
                .WithObject(storedFileName);

            await _minioClient.RemoveObjectAsync(removeArgs);

            _logger.LogInformation(
                "Đã xóa file '{StoredName}' khỏi MinIO bucket '{Bucket}'",
                storedFileName, bucketName);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Lỗi khi xóa file '{StoredName}' khỏi MinIO bucket '{Bucket}'",
                storedFileName, bucketName);
            return false;
        }
    }

    /// <summary>
    /// Tạo presigned URL download (mặc định 3600 giây = 1 giờ).
    /// </summary>
    public async Task<string> GetPresignedUrlAsync(
        string storedFileName,
        string bucketName,
        int expirySeconds = 3600)
    {
        var presignedArgs = new PresignedGetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(storedFileName)
            .WithExpiry(expirySeconds);

        var url = await _minioClient.PresignedGetObjectAsync(presignedArgs);

        _logger.LogInformation(
            "Đã tạo presigned URL cho file '{StoredName}' bucket '{Bucket}', hết hạn sau {Expiry}s",
            storedFileName, bucketName, expirySeconds);

        return url;
    }

    /// <summary>
    /// Tạo bucket nếu chưa tồn tại.
    /// </summary>
    public async Task EnsureBucketExistsAsync(string bucketName)
    {
        var existsArgs = new BucketExistsArgs().WithBucket(bucketName);
        var exists = await _minioClient.BucketExistsAsync(existsArgs);

        if (!exists)
        {
            var makeArgs = new MakeBucketArgs().WithBucket(bucketName);
            await _minioClient.MakeBucketAsync(makeArgs);

            _logger.LogInformation("Đã tạo MinIO bucket '{Bucket}'", bucketName);
        }
    }
}
