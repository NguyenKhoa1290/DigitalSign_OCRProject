namespace DocumentService.Core.DTOs;

public class DocumentProcessDto
{
    public Guid Id { get; set; }
    public Guid DocId { get; set; }
    public Guid FromUserId { get; set; }
    public Guid? ToUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime Timestamp { get; set; }
}
