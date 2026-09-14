namespace DocumentService.Core.DTOs;

/// <summary>
/// Văn thư cập nhật kết quả OCR sau khi upload file
/// </summary>
public class UpdateOcrDto
{
    public string? DocNumber { get; set; }
    public string? Title { get; set; }
    public DateOnly? IssuedDate { get; set; }
    public string? OcrDataRaw { get; set; } // JSON từ OCR Service
}
