namespace SignService.Core.Entities;

public class UserCertificate
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public byte[] CertificatePfx { get; set; } = Array.Empty<byte>(); // PKCS#12 PFX
    public string CertificateThumbprint { get; set; } = string.Empty;
    public DateTime NotBefore { get; set; }
    public DateTime NotAfter { get; set; }
}
