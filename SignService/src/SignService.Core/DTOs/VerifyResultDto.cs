namespace SignService.Core.DTOs;

public class VerifyResultDto
{
    public Guid DocId { get; set; }
    public bool IsValid { get; set; }
    public int TotalSignatures { get; set; }
    public bool CoversWholeDocument { get; set; }
    public string? Message { get; set; }
    public bool HasPersonalSignature { get; set; }
    public bool HasLegalSeal { get; set; }
    public bool HasIncomingDirectionSignature { get; set; }
    public bool HasIncomingAssignmentSignature { get; set; }
    public List<SignatureVerifyInfo> Signatures { get; set; } = new();
}

public class SignatureVerifyInfo
{
    public string SignerName { get; set; } = string.Empty;
    public string SignatureType { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
    public bool IsIntact { get; set; }
    public bool IsCertificateValid { get; set; }
    public bool IsCertificateTrusted { get; set; }
    public bool IsRevoked { get; set; }
}
