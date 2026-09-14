namespace SignService.Core.DTOs;

public class CertificateDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Thumbprint { get; set; } = string.Empty;
    public DateTime NotBefore { get; set; }
    public DateTime NotAfter { get; set; }
    public bool IsValid { get; set; }
    public string Status { get; set; } = string.Empty;
}
