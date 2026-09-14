namespace DocumentService.Core.DTOs;

public class DocumentDto
{
    public Guid Id { get; set; }
    public string? DocNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateOnly? IssuedDate { get; set; }
    public string MinioPath { get; set; } = string.Empty;
    public string? OcrDataRaw { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusDisplay { get; set; } = string.Empty;
    public Guid DocTypeId { get; set; }
    public string? DocTypeName { get; set; }
    public List<DocumentProcessDto> Processes { get; set; } = new();
}
