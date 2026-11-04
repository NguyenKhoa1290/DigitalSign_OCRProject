namespace HauDocumentApp.Models;

public class OcrJobDto
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string Status { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string Pages { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
    public bool IsActive => Status is "Queued" or "Running";
}
