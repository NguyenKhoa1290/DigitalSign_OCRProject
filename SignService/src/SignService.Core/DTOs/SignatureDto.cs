namespace SignService.Core.DTOs;

public class SignatureDto
{
    public Guid Id { get; set; }
    public Guid DocId { get; set; }
    public Guid SignerId { get; set; }
    public string SignatureType { get; set; } = string.Empty;
    public string SignatureTypeDisplay { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
}
