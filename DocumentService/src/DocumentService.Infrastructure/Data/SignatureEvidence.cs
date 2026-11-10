namespace DocumentService.Infrastructure.Data;

// Read-only projection; SignService owns this table and its migrations.
public class SignatureEvidence
{
    public Guid SignerId { get; set; }
    public DateTime SignedAt { get; set; }
    public string SignatureType { get; set; } = "";
    public Guid DocId { get; set; }
    public string? SignedFilePath { get; set; }
}
