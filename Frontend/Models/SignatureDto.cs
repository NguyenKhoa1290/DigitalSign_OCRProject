namespace HauDocumentApp.Models;

public class SignatureDto
{
    public Guid Id { get; set; }
    public Guid DocId { get; set; }
    public Guid DocumentId
    {
        get => DocId;
        set => DocId = value;
    }
    public Guid SignerId { get; set; }
    public string SignerName { get; set; } = string.Empty;
    public string SignerRole { get; set; } = string.Empty;
    public string SignatureType { get; set; } = string.Empty;
    public string SignatureTypeDisplay { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
    public bool IsValid { get; set; } = true;
    public string? CertificateThumbprint { get; set; }
    public string? CertificateSubject { get; set; }
    public DateTime? CertificateExpiry { get; set; }
    public string? SignatureImageUrl { get; set; }
}

public class SignRequestDto
{
    public Guid DocId { get; set; }
    public Guid SignerId { get; set; }
    public string? SignerName { get; set; }
    public string? Reason { get; set; }

    // Các field dưới đây phục vụ UI hiện tại; backend SignService sẽ bỏ qua nếu không cần.
    public string SignatureType { get; set; } = string.Empty;
    public string? PinCode { get; set; }
    public string? Comment { get; set; }
}

public class SignResultDto
{
    public Guid SignatureId { get; set; }
    public Guid DocId { get; set; }
    public Guid SignerId { get; set; }
    public string SignatureType { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
    public string Message { get; set; } = string.Empty;
}
