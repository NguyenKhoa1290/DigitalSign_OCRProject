namespace DocumentService.Core.Interfaces;

/// <summary>
/// Interface gửi sự kiện bất đồng bộ lên Kafka Message Bus.
/// </summary>
public interface IKafkaProducerService
{
    /// <summary>
    /// Gửi sự kiện "DocumentUploaded" để kích hoạt OCR Service xử lý tự động.
    /// </summary>
    /// <param name="docId">ID của văn bản vừa upload.</param>
    /// <param name="minioPath">Đường dẫn file trên MinIO (objectName).</param>
    /// <param name="authToken">JWT token để OCR Service gọi lại DocumentService.</param>
    Task PublishDocumentUploadedAsync(Guid docId, string minioPath, string authToken);
}
