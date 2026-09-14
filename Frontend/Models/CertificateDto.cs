namespace HauDocumentApp.Models;

public class CertificateDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Thumbprint { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public bool IsActive { get; set; }
    public bool IsRevoked { get; set; }
    public string CertificateType { get; set; } = string.Empty;
}

public class IssueCertificateDto
{
    public Guid UserId { get; set; }
    public string CertificateType { get; set; } = "Personal";
    public int ValidityDays { get; set; } = 365;
    public string? KeyPassword { get; set; }
}
