using DocumentService.Core.Common;
using DocumentService.Core.DTOs;
using DocumentService.Core.Entities;
using DocumentService.Core.Exceptions;
using DocumentService.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DocumentService.Infrastructure.Services;

public class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentTypeRepository _documentTypeRepository;
    private readonly IDocumentProcessRepository _documentProcessRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly IKafkaProducerService _kafkaProducer;
    private readonly ILogger<DocumentService> _logger;

    private const string BucketName = "documents";

    public DocumentService(
        IDocumentRepository documentRepository,
        IDocumentTypeRepository documentTypeRepository,
        IDocumentProcessRepository documentProcessRepository,
        IFileStorageService fileStorageService,
        IKafkaProducerService kafkaProducer,
        ILogger<DocumentService> logger)
    {
        _documentRepository = documentRepository;
        _documentTypeRepository = documentTypeRepository;
        _documentProcessRepository = documentProcessRepository;
        _fileStorageService = fileStorageService;
        _kafkaProducer = kafkaProducer;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CRUD
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<DocumentDto> CreateDocumentAsync(CreateDocumentDto dto, Guid userId)
    {
        // Kiểm tra DocType tồn tại
        var docType = await _documentTypeRepository.GetByIdAsync(dto.DocTypeId)
            ?? throw new DocumentServiceException($"Loại văn bản với ID '{dto.DocTypeId}' không tồn tại.");

        var document = new Document
        {
            Id = Guid.NewGuid(),
            Title = dto.Title.Trim(),
            DocTypeId = dto.DocTypeId,
            DocNumber = dto.DocNumber?.Trim(),
            IssuedDate = dto.IssuedDate,
            MinioPath = string.Empty,    // sẽ cập nhật sau khi upload
            Status = DocumentStatus.Draft
        };

        var created = await _documentRepository.CreateAsync(document);

        // Ghi lại process ban đầu
        await _documentProcessRepository.CreateAsync(new DocumentProcess
        {
            DocId = created.Id,
            FromUserId = userId,
            Action = DocumentAction.Submit,
            Comment = "Tạo mới văn bản.",
            Timestamp = DateTime.UtcNow
        });

        _logger.LogInformation("Tạo văn bản mới ID={Id}, Title={Title}", created.Id, created.Title);

        // Reload để có DocType navigation
        var full = await _documentRepository.GetByIdAsync(created.Id, includeProcesses: true)
                   ?? created;
        return MapToDto(full);
    }

    public async Task<DocumentDto> GetDocumentByIdAsync(Guid id)
    {
        var document = await _documentRepository.GetByIdAsync(id, includeProcesses: true)
            ?? throw new DocumentNotFoundException(id);

        return MapToDto(document);
    }

    public async Task<PagedResult<DocumentDto>> GetAllDocumentsAsync(DocumentQueryParams query)
    {
        var (documents, total) = await _documentRepository.GetAllAsync(query);
        var dtos = documents.Select(MapToDto).ToList();

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        return new PagedResult<DocumentDto>(dtos, total, page, pageSize);
    }

    public async Task DeleteDocumentAsync(Guid id)
    {
        var document = await _documentRepository.GetByIdAsync(id)
            ?? throw new DocumentNotFoundException(id);

        if (document.Status != DocumentStatus.Draft && document.Status != DocumentStatus.Rejected)
            throw new InvalidWorkflowTransitionException(
                document.Status,
                "Delete (chỉ được xóa khi ở trạng thái Draft hoặc Rejected)");

        await _documentRepository.DeleteAsync(id);
        _logger.LogInformation("Đã xóa văn bản ID={Id}", id);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // File Upload
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<DocumentDto> UploadFileAsync(
        Guid id,
        Stream fileStream,
        string fileName,
        string contentType,
        Guid userId)
    {
        var document = await _documentRepository.GetByIdAsync(id, includeProcesses: false)
            ?? throw new DocumentNotFoundException(id);

        // 1. Upload lên MinIO
        var storedFileName = await _fileStorageService.UploadFileAsync(
            fileStream, fileName, contentType, BucketName);

        // 2. Cập nhật MinioPath: "documents/{filename}"
        document.MinioPath = $"{BucketName}/{storedFileName}";
        var updated = await _documentRepository.UpdateAsync(document);

        _logger.LogInformation("Upload file cho văn bản ID={Id}, path={Path}", id, document.MinioPath);

        // 3. Đẩy event lên Kafka → OCR Service tự động xử lý (bất đồng bộ)
        //    - Không throw nếu Kafka lỗi (upload vẫn thành công)
        //    - minioPath chỉ là objectName (không có bucket prefix) cho OCR Service
        await _kafkaProducer.PublishDocumentUploadedAsync(
            docId:     updated.Id,
            minioPath: storedFileName,          // objectName trên MinIO
            authToken: string.Empty);           // token sẽ được OCR Service lấy riêng

        var full = await _documentRepository.GetByIdAsync(updated.Id, includeProcesses: true)
                   ?? updated;
        return MapToDto(full);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // OCR
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<DocumentDto> UpdateOcrDataAsync(Guid id, UpdateOcrDto dto, Guid userId)
    {
        var document = await _documentRepository.GetByIdAsync(id, includeProcesses: false)
            ?? throw new DocumentNotFoundException(id);

        if (dto.DocNumber is not null)
            document.DocNumber = dto.DocNumber.Trim();

        if (dto.Title is not null)
            document.Title = dto.Title.Trim();

        if (dto.IssuedDate.HasValue)
            document.IssuedDate = dto.IssuedDate;

        if (dto.OcrDataRaw is not null)
            document.OcrDataRaw = dto.OcrDataRaw;

        var updated = await _documentRepository.UpdateAsync(document);

        // Ghi log process
        await _documentProcessRepository.CreateAsync(new DocumentProcess
        {
            DocId = id,
            FromUserId = userId,
            Action = DocumentAction.UpdateOCR,
            Comment = "Cập nhật kết quả OCR.",
            Timestamp = DateTime.UtcNow
        });

        _logger.LogInformation("Cập nhật OCR cho văn bản ID={Id}", id);

        var full = await _documentRepository.GetByIdAsync(updated.Id, includeProcesses: true)
                   ?? updated;
        return MapToDto(full);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Workflow
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<DocumentDto> SubmitForReviewAsync(Guid id, Guid userId, string? comment)
    {
        var document = await _documentRepository.GetByIdAsync(id, includeProcesses: false)
            ?? throw new DocumentNotFoundException(id);

        if (document.Status != DocumentStatus.Draft)
            throw new InvalidWorkflowTransitionException(document.Status, DocumentStatus.PendingDeptReview);

        document.Status = DocumentStatus.PendingDeptReview;
        var updated = await _documentRepository.UpdateAsync(document);

        await _documentProcessRepository.CreateAsync(new DocumentProcess
        {
            DocId = id,
            FromUserId = userId,
            Action = DocumentAction.Submit,
            Comment = comment ?? "Trình văn bản để xét duyệt.",
            Timestamp = DateTime.UtcNow
        });

        _logger.LogInformation("Văn bản ID={Id} chuyển sang PendingDeptReview", id);

        var full = await _documentRepository.GetByIdAsync(updated.Id, includeProcesses: true)
                   ?? updated;
        return MapToDto(full);
    }

    public async Task<DocumentDto> DeptSignAsync(Guid id, Guid userId, string? comment)
    {
        var document = await _documentRepository.GetByIdAsync(id, includeProcesses: false)
            ?? throw new DocumentNotFoundException(id);

        if (document.Status != DocumentStatus.PendingDeptReview)
            throw new InvalidWorkflowTransitionException(document.Status, DocumentStatus.DeptSigned);

        // DeptSigned → tự động chuyển sang PendingDirectorSign
        document.Status = DocumentStatus.PendingDirectorSign;
        var updated = await _documentRepository.UpdateAsync(document);

        await _documentProcessRepository.CreateAsync(new DocumentProcess
        {
            DocId = id,
            FromUserId = userId,
            Action = DocumentAction.DeptSign,
            Comment = comment ?? "Lãnh đạo phòng ký nháy.",
            Timestamp = DateTime.UtcNow
        });

        _logger.LogInformation("Văn bản ID={Id} được ký nháy, chuyển sang PendingDirectorSign", id);

        var full = await _documentRepository.GetByIdAsync(updated.Id, includeProcesses: true)
                   ?? updated;
        return MapToDto(full);
    }

    public async Task<DocumentDto> DirectorSignAsync(Guid id, Guid userId, string? comment)
    {
        var document = await _documentRepository.GetByIdAsync(id, includeProcesses: false)
            ?? throw new DocumentNotFoundException(id);

        if (document.Status != DocumentStatus.PendingDirectorSign)
            throw new InvalidWorkflowTransitionException(document.Status, DocumentStatus.DirectorSigned);

        document.Status = DocumentStatus.DirectorSigned;
        var updated = await _documentRepository.UpdateAsync(document);

        await _documentProcessRepository.CreateAsync(new DocumentProcess
        {
            DocId = id,
            FromUserId = userId,
            Action = DocumentAction.DirectorSign,
            Comment = comment ?? "Ban Giám hiệu ký số.",
            Timestamp = DateTime.UtcNow
        });

        _logger.LogInformation("Văn bản ID={Id} được BGH ký số, chuyển sang DirectorSigned", id);

        var full = await _documentRepository.GetByIdAsync(updated.Id, includeProcesses: true)
                   ?? updated;
        return MapToDto(full);
    }

    public async Task<DocumentDto> RejectAsync(Guid id, Guid userId, string reason)
    {
        var document = await _documentRepository.GetByIdAsync(id, includeProcesses: false)
            ?? throw new DocumentNotFoundException(id);

        // Có thể reject ở bất kỳ trạng thái pending nào
        var allowedStatuses = new[]
        {
            DocumentStatus.PendingDeptReview,
            DocumentStatus.DeptSigned,
            DocumentStatus.PendingDirectorSign
        };

        if (!allowedStatuses.Contains(document.Status))
            throw new InvalidWorkflowTransitionException(document.Status, DocumentStatus.Rejected);

        document.Status = DocumentStatus.Rejected;
        var updated = await _documentRepository.UpdateAsync(document);

        await _documentProcessRepository.CreateAsync(new DocumentProcess
        {
            DocId = id,
            FromUserId = userId,
            Action = DocumentAction.Reject,
            Comment = reason,
            Timestamp = DateTime.UtcNow
        });

        _logger.LogInformation("Văn bản ID={Id} bị từ chối. Lý do: {Reason}", id, reason);

        var full = await _documentRepository.GetByIdAsync(updated.Id, includeProcesses: true)
                   ?? updated;
        return MapToDto(full);
    }

    public async Task<DocumentDto> PublishAsync(Guid id, Guid userId)
    {
        var document = await _documentRepository.GetByIdAsync(id, includeProcesses: false)
            ?? throw new DocumentNotFoundException(id);

        if (document.Status != DocumentStatus.DirectorSigned)
            throw new InvalidWorkflowTransitionException(document.Status, DocumentStatus.Published);

        document.Status = DocumentStatus.Published;
        var updated = await _documentRepository.UpdateAsync(document);

        await _documentProcessRepository.CreateAsync(new DocumentProcess
        {
            DocId = id,
            FromUserId = userId,
            Action = DocumentAction.Publish,
            Comment = "Văn thư phát hành văn bản.",
            Timestamp = DateTime.UtcNow
        });

        _logger.LogInformation("Văn bản ID={Id} đã được phát hành", id);

        var full = await _documentRepository.GetByIdAsync(updated.Id, includeProcesses: true)
                   ?? updated;
        return MapToDto(full);
    }

    public async Task<DocumentDto> AssignAsync(Guid id, Guid fromUserId, Guid toUserId, string? comment)
    {
        var document = await _documentRepository.GetByIdAsync(id, includeProcesses: false)
            ?? throw new DocumentNotFoundException(id);

        await _documentProcessRepository.CreateAsync(new DocumentProcess
        {
            DocId = id,
            FromUserId = fromUserId,
            ToUserId = toUserId,
            Action = DocumentAction.Assign,
            Comment = comment ?? "Phân công xử lý văn bản.",
            Timestamp = DateTime.UtcNow
        });

        _logger.LogInformation("Văn bản ID={Id} phân công từ {From} → {To}", id, fromUserId, toUserId);

        var full = await _documentRepository.GetByIdAsync(document.Id, includeProcesses: true)
                   ?? document;
        return MapToDto(full);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DocumentTypes
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<IEnumerable<DocumentTypeDto>> GetDocumentTypesAsync()
    {
        var types = await _documentTypeRepository.GetAllAsync();
        return types.Select(t => new DocumentTypeDto
        {
            Id = t.Id,
            TypeName = t.TypeName,
            Description = t.Description
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Mapping helper
    // ──────────────────────────────────────────────────────────────────────────

    private static DocumentDto MapToDto(Document doc) => new DocumentDto
    {
        Id = doc.Id,
        DocNumber = doc.DocNumber,
        Title = doc.Title,
        IssuedDate = doc.IssuedDate,
        MinioPath = doc.MinioPath,
        OcrDataRaw = doc.OcrDataRaw,
        Status = doc.Status,
        StatusDisplay = DocumentStatus.GetDisplayName(doc.Status),
        DocTypeId = doc.DocTypeId,
        DocTypeName = doc.DocType?.TypeName,
        Processes = doc.Processes
            .OrderBy(p => p.Timestamp)
            .Select(p => new DocumentProcessDto
            {
                Id = p.Id,
                DocId = p.DocId,
                FromUserId = p.FromUserId,
                ToUserId = p.ToUserId,
                Action = p.Action,
                Comment = p.Comment,
                Timestamp = p.Timestamp
            })
            .ToList()
    };
}
