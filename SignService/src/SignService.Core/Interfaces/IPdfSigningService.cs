namespace SignService.Core.Interfaces;

public interface IPdfSigningService
{
    // Ký PDF với certificate của user, trả về byte[] PDF đã ký
    Task<byte[]> SignPdfAsync(byte[] pdfBytes, Guid userId, string signatureType, string signerName, string reason);

    // Xác minh chữ ký trên PDF
    Task<SignVerifyResult> VerifyPdfSignaturesAsync(byte[] pdfBytes);
}

public class SignVerifyResult
{
    public bool IsValid { get; set; }
    public int SignatureCount { get; set; }
    public List<SignatureInfo> Signatures { get; set; } = new();
}

public class SignatureInfo
{
    public string SignerName { get; set; } = string.Empty;
    public string SignatureType { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
    public bool IsIntact { get; set; } // PDF không bị sửa sau khi ký
    public bool IsCertificateValid { get; set; }
}
