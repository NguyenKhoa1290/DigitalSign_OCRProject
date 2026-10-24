namespace DocumentService.Core.Entities;

// Durable administrative audit and file-cleanup queue; deliberately no FK to the deleted document.
public class DocumentPurgeRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocId { get; set; }
    public string Title { get; set; } = "";
    public string OriginalStatus { get; set; } = "";
    public Guid OriginalVersion { get; set; }
    public Guid ActorId { get; set; }
    public string Reason { get; set; } = "";
    public DateTime PurgedAt { get; set; }
    public string ObjectNamesJson { get; set; } = "[]";
    public DateTime? FilesCleanedAt { get; set; }
    public int CleanupAttempts { get; set; }
    public string? CleanupError { get; set; }
}
