using DocumentService.Core.Common;
using DocumentService.Core.DTOs;

namespace DocumentService.Core.Interfaces;

public interface IDocumentService
{
    // CRUD
    Task<DocumentDto> CreateDocumentAsync(CreateDocumentDto dto, Guid userId);
    Task<DocumentDto> GetDocumentByIdAsync(Guid id);
    Task<PagedResult<DocumentDto>> GetAllDocumentsAsync(DocumentQueryParams query);
    Task DeleteDocumentAsync(Guid id);

    // File
    Task<DocumentDto> UploadFileAsync(Guid id, Stream fileStream, string fileName, string contentType, Guid userId);

    // OCR
    Task<DocumentDto> UpdateOcrDataAsync(Guid id, UpdateOcrDto dto, Guid userId);

    // Workflow
    Task<DocumentDto> SubmitForReviewAsync(Guid id, Guid userId, string? comment);
    Task<DocumentDto> DeptSignAsync(Guid id, Guid userId, string? comment);         // Lãnh đạo phòng ký nháy
    Task<DocumentDto> DirectorSignAsync(Guid id, Guid userId, string? comment);     // BGH ký số
    Task<DocumentDto> RejectAsync(Guid id, Guid userId, string reason);             // Từ chối ở bất kỳ bước
    Task<DocumentDto> PublishAsync(Guid id, Guid userId);                           // Văn thư phát hành
    Task<DocumentDto> AssignAsync(Guid id, Guid fromUserId, Guid toUserId, string? comment); // Phân công

    // DocumentTypes
    Task<IEnumerable<DocumentTypeDto>> GetDocumentTypesAsync();
}
