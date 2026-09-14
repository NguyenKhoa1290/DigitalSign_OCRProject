namespace SignService.Core.DTOs;

public class SignResultDto
{
    public Guid SignatureId { get; set; }
    public Guid DocId { get; set; }
    public Guid SignerId { get; set; }
    public string SignatureType { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
    public string Message { get; set; } = string.Empty;
}
