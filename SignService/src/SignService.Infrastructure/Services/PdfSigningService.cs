using iText.Bouncycastle.Crypto;
using iText.Bouncycastle.X509;
using iText.Commons.Bouncycastle.Cert;
using iText.Kernel.Pdf;
using iText.Signatures;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Pkcs;
using SignService.Core.Exceptions;
using SignService.Core.Interfaces;

namespace SignService.Infrastructure.Services;

public class PdfSigningService : IPdfSigningService
{
    private readonly ICertificateService _certService;
    private readonly ILogger<PdfSigningService> _logger;

    public PdfSigningService(ICertificateService certService, ILogger<PdfSigningService> logger)
    {
        _certService = certService;
        _logger = logger;
    }

    public async Task<byte[]> SignPdfAsync(
        byte[] pdfBytes,
        Guid userId,
        string signatureType,
        string signerName,
        string reason)
    {
        _logger.LogInformation("Bắt đầu ký PDF cho user {UserId}, loại: {Type}", userId, signatureType);

        var userCert = await _certService.GetUserCertificateAsync(userId)
                       ?? throw new CertificateNotFoundException(userId);

        if (!_certService.IsCertificateValid(userCert))
            throw new CertificateExpiredException();

        try
        {
            return await Task.Run(() =>
            {
                // Load PFX
                var password = userId.ToString();
                using var pfxStream = new MemoryStream(userCert.CertificatePfx);
                var pkcs12Store = new Pkcs12StoreBuilder().Build();
                pkcs12Store.Load(pfxStream, password.ToCharArray());

                string? alias = null;
                foreach (string a in pkcs12Store.Aliases)
                {
                    if (pkcs12Store.IsKeyEntry(a))
                    {
                        alias = a;
                        break;
                    }
                }

                if (alias == null)
                    throw new PdfSigningException("Không tìm thấy private key trong PFX.");

                var bcPrivateKey = pkcs12Store.GetKey(alias).Key;
                var chainEntries = pkcs12Store.GetCertificateChain(alias);

                // Wrap BouncyCastle key/certs into iText7 v8 adapter types
                var iTextPrivateKey = new PrivateKeyBC(bcPrivateKey);

                var chain = new List<IX509Certificate>();
                if (chainEntries != null && chainEntries.Length > 0)
                {
                    foreach (var entry in chainEntries)
                        chain.Add(new X509CertificateBC(entry.Certificate));
                }
                else
                {
                    var certEntry = pkcs12Store.GetCertificate(alias);
                    chain.Add(new X509CertificateBC(certEntry.Certificate));
                }

                // Sign PDF
                using var inputMs = new MemoryStream(pdfBytes);
                using var outputMs = new MemoryStream();

                var reader = new PdfReader(inputMs);
                var stamperProps = new StampingProperties().UseAppendMode();
                var signer = new PdfSigner(reader, outputMs, stamperProps);

                // iText7 v8 - set signing metadata directly on signer
                signer.SetReason(reason);
                signer.SetLocation("Hanoi, Vietnam");
                signer.SetContact(signerName);
                signer.SetFieldName($"Sig_{signatureType}_{DateTime.UtcNow:yyyyMMddHHmmss}");

                IExternalSignature externalSignature = new PrivateKeySignature(iTextPrivateKey, DigestAlgorithms.SHA256);

                signer.SignDetached(
                    externalSignature,
                    chain.ToArray(),
                    null,
                    null,
                    null,
                    0,
                    PdfSigner.CryptoStandard.CMS);

                _logger.LogInformation("PDF đã được ký thành công cho user {UserId}", userId);
                return outputMs.ToArray();
            });
        }
        catch (SignServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi ký PDF cho user {UserId}", userId);
            throw new PdfSigningException(ex.Message);
        }
    }

    public async Task<SignVerifyResult> VerifyPdfSignaturesAsync(byte[] pdfBytes)
    {
        return await Task.Run(() =>
        {
            var result = new SignVerifyResult();

            try
            {
                using var ms = new MemoryStream(pdfBytes);
                using var reader = new PdfReader(ms);
                using var pdfDoc = new PdfDocument(reader);

                var signUtil = new SignatureUtil(pdfDoc);
                var sigNames = signUtil.GetSignatureNames();

                result.SignatureCount = sigNames.Count;

                foreach (var sigName in sigNames)
                {
                    try
                    {
                        var pkcs7 = signUtil.ReadSignatureData(sigName);
                        bool intact = pkcs7.VerifySignatureIntegrityAndAuthenticity();

                        var signerCert = pkcs7.GetSigningCertificate(); // returns IX509Certificate
                        bool certValid = true;
                        string signerName = "Unknown";
                        DateTime signedAt = DateTime.UtcNow;

                        if (signerCert != null)
                        {
                            try
                            {
                                signerCert.CheckValidity(DateTime.UtcNow); // IX509Certificate.CheckValidity(DateTime)
                            }
                            catch
                            {
                                certValid = false;
                            }

                            // IX509Certificate.GetSubjectDN() returns IX500Name
                            var subjectDn = signerCert.GetSubjectDN()?.ToString() ?? string.Empty;
                            signerName = ExtractCn(subjectDn) ?? subjectDn;
                        }

                        // PdfPKCS7.GetSignDate() returns DateTime struct
                        var signDate = pkcs7.GetSignDate();
                        if (signDate != default)
                            signedAt = signDate.ToUniversalTime();

                        result.Signatures.Add(new SignatureInfo
                        {
                            SignerName = signerName,
                            SignatureType = ExtractSignatureType(sigName),
                            SignedAt = signedAt,
                            IsIntact = intact,
                            IsCertificateValid = certValid
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Không thể xác minh chữ ký: {SigName}", sigName);
                        result.Signatures.Add(new SignatureInfo
                        {
                            SignerName = sigName,
                            IsIntact = false,
                            IsCertificateValid = false
                        });
                    }
                }

                result.IsValid = result.Signatures.Count > 0 && result.Signatures.All(s => s.IsIntact);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xác minh chữ ký PDF");
                result.IsValid = false;
            }

            return result;
        });
    }

    private static string ExtractSignatureType(string fieldName)
    {
        if (fieldName.Contains("PersonalSignature", StringComparison.OrdinalIgnoreCase))
            return Core.Entities.SignatureType.PersonalSignature;
        if (fieldName.Contains("LegalSeal", StringComparison.OrdinalIgnoreCase))
            return Core.Entities.SignatureType.LegalSeal;
        return "Unknown";
    }

    private static string? ExtractCn(string dn)
    {
        var parts = dn.Split(',');
        foreach (var part in parts)
        {
            var kv = part.Trim().Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim().Equals("CN", StringComparison.OrdinalIgnoreCase))
                return kv[1].Trim();
        }
        return null;
    }
}
