namespace DocumentService.Core.Entities;

public class Document
{
    public Guid Id { get; set; }
    public string? DocNumber { get; set; }           // VD: 123/QD-HAU, do OCR bóc tách
    public string Title { get; set; } = string.Empty;
    public DateOnly? IssuedDate { get; set; }
    public string MinioPath { get; set; } = string.Empty;
    public string? OcrDataRaw { get; set; }          // JSON string từ OCR Service
    public string Status { get; set; } = DocumentStatus.Draft;
    public Guid DocTypeId { get; set; }

    // Navigation
    public DocumentType? DocType { get; set; }
    public ICollection<DocumentProcess> Processes { get; set; } = new List<DocumentProcess>();
}
