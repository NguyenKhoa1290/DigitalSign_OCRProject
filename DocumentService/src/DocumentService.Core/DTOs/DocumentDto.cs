namespace DocumentService.Core.DTOs;

public class DocumentDto
{
    public Guid Id { get; set; }
    public string? DocNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateOnly? IssuedDate { get; set; }
    public DateTime? CreatedAt { get; set; }
    public Guid? CreatedById { get; set; }
    public Guid ConcurrencyVersion { get; set; }
    public bool CanDeleteDraft { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
    public string? DeleteReason { get; set; }
    public DateTime? OfficiallyReceivedAt { get; set; }
    public Guid? ProcessingDepartmentId { get; set; }
    public string? ProcessingDepartmentName { get; set; }
    public Guid? AssignedToId { get; set; }
    public string? AssignedToName { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string MinioPath { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string FileContentType { get; set; } = "application/pdf";
    public string? OcrDataRaw { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusDisplay { get; set; } = string.Empty;
    public Guid DocTypeId { get; set; }
    public string? DocTypeName { get; set; }
    public List<DocumentProcessDto> Processes { get; set; } = new();
}
