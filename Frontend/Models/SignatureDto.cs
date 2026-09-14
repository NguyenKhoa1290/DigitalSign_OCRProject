namespace HauDocumentApp.Models;

public class SignatureDto
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string SignerName { get; set; } = string.Empty;
    public string SignerRole { get; set; } = string.Empty;
    public string SignatureType { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
    public bool IsValid { get; set; }
    public string? CertificateThumbprint { get; set; }
    public string? CertificateSubject { get; set; }
    public DateTime? CertificateExpiry { get; set; }
    public string? SignatureImageUrl { get; set; }
}

public class SignRequestDto
{
    public string SignatureType { get; set; } = string.Empty;
    public string? PinCode { get; set; }
    public string? Comment { get; set; }
}
