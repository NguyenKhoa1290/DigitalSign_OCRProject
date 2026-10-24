namespace DocumentService.Core.Entities;

public class Document
{
    public Guid Id { get; set; }
    public string? DocNumber { get; set; }           // VD: 123/QD-HAU, do OCR bóc tách
    public string Title { get; set; } = string.Empty;
    public DateOnly? IssuedDate { get; set; }
    public string MinioPath { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? OcrDataRaw { get; set; }          // JSON string từ OCR Service
    public string Status { get; set; } = DocumentStatus.Draft;
    public Guid DocTypeId { get; set; }
    public Guid? CreatedById { get; set; }
    public DateTime? CreatedAt { get; set; }
    // Legacy rows remain blocked until their origin and workflow are reconciled.
    public bool DeletionEligibilityConfirmed { get; set; }
    public DateTime? FirstSubmittedAt { get; set; }
    public DateTime? FirstAssignedAt { get; set; }
    public DateTime? OfficiallyReceivedAt { get; set; }
    public DateTime? SigningStartedAt { get; set; }
    public Guid? ProcessingDepartmentId { get; set; }
    public string? ProcessingDepartmentName { get; set; }
    public Guid? AssignedToId { get; set; }
    public string? AssignedToName { get; set; }
    public string? IncomingOriginalPath { get; set; }
    public string? IncomingDirectionPath { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
    public string? DeleteReason { get; set; }
    public Guid ConcurrencyVersion { get; set; } = Guid.NewGuid();

    // Navigation
    public DocumentType? DocType { get; set; }
    public ICollection<DocumentProcess> Processes { get; set; } = new List<DocumentProcess>();
}
