namespace HauDocumentApp.Models;

public class DocumentDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? DocumentNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DocumentTypeName { get; set; } = string.Empty;
    public Guid? DocumentTypeId { get; set; }
    public DateTime? IssuedDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedByName { get; set; }
    public Guid? CreatedById { get; set; }
    public string? DepartmentName { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? FileUrl { get; set; }
    public string? FileName { get; set; }
    public string? OcrText { get; set; }
    public List<DocumentProcessDto> ProcessHistory { get; set; } = new();
    public List<SignatureDto> Signatures { get; set; } = new();
}

public class DocumentProcessDto
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public string? ProcessedByName { get; set; }
    public DateTime ProcessedAt { get; set; }
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
}
