namespace SignService.Core.Interfaces;

/// <summary>
/// Read-only lookup sang bảng Documents do DocumentService quản lý.
/// SignService chỉ cần MinioPath thật để tải/lưu PDF đã ký đúng object.
/// </summary>
public interface IDocumentFileRepository
{
    Task<string?> GetMinioPathAsync(Guid docId);
    Task<bool> IsIncomingAsync(Guid docId);
    Task<IDocumentSigningLease> BeginSigningAsync(Guid docId);
    Task SaveSignedPdfAsync(Guid docId, string objectName);
    Task ValidateSigningStageAsync(Guid docId, string signatureType);
}

public interface IDocumentSigningLease : IAsyncDisposable
{
    Task CompleteAsync();
}

