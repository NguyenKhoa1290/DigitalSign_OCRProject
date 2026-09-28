namespace HauDocumentApp.Models;

public class CertificateDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string CertificateType { get; set; } = string.Empty;
    public string Thumbprint { get; set; } = string.Empty;
    public DateTime NotBefore { get; set; }
    public DateTime NotAfter { get; set; }
    public bool IsValid { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class IssueCertificateDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string CertificateType { get; set; } = "Personal";
    public int ValidityDays { get; set; } = 365;
}

public class IssueOwnCertificateDto
{
    public int ValidityDays { get; set; } = 365;
}
