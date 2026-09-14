namespace SignService.Core.Entities;

public class Signature
{
    public Guid Id { get; set; }
    public Guid DocId { get; set; }
    public Guid SignerId { get; set; }
    public string SignatureType { get; set; } = string.Empty; // PersonalSignature | LegalSeal
    public DateTime SignedAt { get; set; } = DateTime.UtcNow;
}
