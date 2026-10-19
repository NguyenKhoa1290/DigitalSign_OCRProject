namespace DocumentService.Infrastructure.Data;

// Read-only projection; SignService owns this table and its migrations.
public class SignatureEvidence
{
    public Guid DocId { get; set; }
}
