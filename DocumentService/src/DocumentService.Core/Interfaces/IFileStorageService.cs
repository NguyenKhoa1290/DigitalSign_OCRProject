namespace DocumentService.Core.Interfaces;

public interface IFileStorageService
{
    /// <summary>
    /// Upload file lên MinIO, trả về tên file đã được lưu (storedFileName = Guid + extension).
    /// </summary>
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType, string bucketName);

    /// <summary>
    /// Download file từ MinIO theo tên file đã lưu và bucket.
    /// </summary>
    Task<Stream> DownloadFileAsync(string storedFileName, string bucketName);

    /// <summary>
    /// Xóa file trên MinIO.
    /// </summary>
    Task<bool> DeleteFileAsync(string storedFileName, string bucketName);

    /// <summary>
    /// Tạo presigned URL để download trực tiếp (mặc định 1 giờ).
    /// </summary>
    Task<string> GetPresignedUrlAsync(string storedFileName, string bucketName, int expirySeconds = 3600);

    /// <summary>
    /// Tạo bucket nếu chưa tồn tại.
    /// </summary>
    Task EnsureBucketExistsAsync(string bucketName);
}
