namespace SignService.Core.Interfaces;

/// <summary>
/// Read-only lookup sang bảng Documents do DocumentService quản lý.
/// SignService chỉ cần MinioPath thật để tải/lưu PDF đã ký đúng object.
/// </summary>
public interface IDocumentFileRepository
{
    Task<string?> GetMinioPathAsync(Guid docId);
}

