using Microsoft.Extensions.Logging;
using SignService.Core.DTOs;
using SignService.Core.Entities;
using SignService.Core.Exceptions;
using SignService.Core.Interfaces;

namespace SignService.Infrastructure.Services;

public class SignService : ISignService
{
    private readonly ISignatureRepository _signatureRepo;
    private readonly ICertificateService _certService;
    private readonly IPdfSigningService _pdfSigningService;
    private readonly IMinioService _minioService;
    private readonly ILogger<SignService> _logger;

    public SignService(
        ISignatureRepository signatureRepo,
        ICertificateService certService,
        IPdfSigningService pdfSigningService,
        IMinioService minioService,
        ILogger<SignService> logger)
    {
        _signatureRepo = signatureRepo;
        _certService = certService;
        _pdfSigningService = pdfSigningService;
        _minioService = minioService;
        _logger = logger;
    }

    public async Task<SignResultDto> PersonalSignAsync(SignRequestDto request)
    {
        return await SignInternalAsync(request, SignatureType.PersonalSignature, requireExisting: null);
    }

    public async Task<SignResultDto> LegalSealAsync(SignRequestDto request)
    {
        // Yêu cầu phải có PersonalSignature trước
        bool hasPersonal = await _signatureRepo.HasSignatureAsync(request.DocId, SignatureType.PersonalSignature);
        if (!hasPersonal)
            throw new SignServiceException("Văn bản chưa có chữ ký nháy. Lãnh đạo Phòng cần ký trước khi ký pháp nhân.");

        return await SignInternalAsync(request, SignatureType.LegalSeal, requireExisting: null);
    }

    private async Task<SignResultDto> SignInternalAsync(SignRequestDto request, string signatureType, string? requireExisting)
    {
        _logger.LogInformation("Bắt đầu ký {Type} cho văn bản {DocId} bởi {SignerId}",
            signatureType, request.DocId, request.SignerId);

        // Kiểm tra cert của người ký
        var userCert = await _certService.GetUserCertificateAsync(request.SignerId)
                       ?? throw new CertificateNotFoundException(request.SignerId);

        if (!_certService.IsCertificateValid(userCert))
            throw new CertificateExpiredException();

        // Kiểm tra đã ký chưa
        bool alreadySigned = await _signatureRepo.HasSignatureAsync(request.DocId, signatureType);
        if (alreadySigned)
            throw new AlreadySignedException(signatureType);

        // Tải PDF từ MinIO
        string objectName = $"{request.DocId}.pdf";
        byte[] pdfBytes;
        try
        {
            pdfBytes = await _minioService.DownloadFileAsync(objectName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không tìm thấy PDF trên MinIO: {ObjectName}", objectName);
            throw new DocumentNotFoundException(request.DocId);
        }

        // Ký PDF
        string signerName = request.SignerName ?? userCert.FullName;
        string reason = request.Reason ?? (signatureType == SignatureType.PersonalSignature
            ? "Ký nháy - Lãnh đạo Phòng"
            : "Ký pháp nhân - Ban Giám hiệu");

        byte[] signedPdf = await _pdfSigningService.SignPdfAsync(
            pdfBytes,
            request.SignerId,
            signatureType,
            signerName,
            reason);

        // Ghi đè MinIO
        await _minioService.UploadFileAsync(objectName, signedPdf, "application/pdf");

        // Lưu DB
        var signature = await _signatureRepo.CreateAsync(new Signature
        {
            DocId = request.DocId,
            SignerId = request.SignerId,
            SignatureType = signatureType,
            SignedAt = DateTime.UtcNow
        });

        _logger.LogInformation("Ký {Type} thành công. SignatureId={Id}", signatureType, signature.Id);

        return new SignResultDto
        {
            SignatureId = signature.Id,
            DocId = signature.DocId,
            SignerId = signature.SignerId,
            SignatureType = signature.SignatureType,
            SignedAt = signature.SignedAt,
            Message = $"Ký {GetTypeDisplay(signatureType)} thành công."
        };
    }

    public async Task<VerifyResultDto> VerifySignaturesAsync(Guid docId)
    {
        _logger.LogInformation("Xác minh chữ ký văn bản {DocId}", docId);

        string objectName = $"{docId}.pdf";
        byte[] pdfBytes;
        try
        {
            pdfBytes = await _minioService.DownloadFileAsync(objectName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không tìm thấy PDF: {ObjectName}", objectName);
            throw new DocumentNotFoundException(docId);
        }

        var pdfVerifyResult = await _pdfSigningService.VerifyPdfSignaturesAsync(pdfBytes);
        var dbSignatures = (await _signatureRepo.GetByDocumentIdAsync(docId)).ToList();

        bool hasPersonal = dbSignatures.Any(s => s.SignatureType == SignatureType.PersonalSignature);
        bool hasLegalSeal = dbSignatures.Any(s => s.SignatureType == SignatureType.LegalSeal);

        var verifyInfoList = pdfVerifyResult.Signatures.Select(s => new SignatureVerifyInfo
        {
            SignerName = s.SignerName,
            SignatureType = s.SignatureType,
            SignedAt = s.SignedAt,
            IsIntact = s.IsIntact
        }).ToList();

        return new VerifyResultDto
        {
            DocId = docId,
            IsValid = pdfVerifyResult.IsValid,
            TotalSignatures = pdfVerifyResult.SignatureCount,
            HasPersonalSignature = hasPersonal,
            HasLegalSeal = hasLegalSeal,
            Signatures = verifyInfoList
        };
    }

    public async Task<IEnumerable<SignatureDto>> GetSignaturesByDocumentAsync(Guid docId)
    {
        var signatures = await _signatureRepo.GetByDocumentIdAsync(docId);
        return signatures.Select(s => new SignatureDto
        {
            Id = s.Id,
            DocId = s.DocId,
            SignerId = s.SignerId,
            SignatureType = s.SignatureType,
            SignatureTypeDisplay = GetTypeDisplay(s.SignatureType),
            SignedAt = s.SignedAt
        });
    }

    public async Task<CertificateDto> IssueCertificateAsync(IssueCertificateDto request)
    {
        var cert = await _certService.IssueCertificateAsync(
            request.UserId,
            request.Username,
            request.FullName,
            request.ValidityYears);

        return ToCertificateDto(cert);
    }

    public async Task<CertificateDto?> GetCertificateAsync(Guid userId)
    {
        var cert = await _certService.GetUserCertificateAsync(userId);
        if (cert == null) return null;
        return ToCertificateDto(cert);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private CertificateDto ToCertificateDto(Core.Entities.UserCertificate cert)
    {
        bool isValid = _certService.IsCertificateValid(cert);
        return new CertificateDto
        {
            UserId = cert.UserId,
            Username = cert.Username,
            FullName = cert.FullName,
            Thumbprint = cert.CertificateThumbprint,
            NotBefore = cert.NotBefore,
            NotAfter = cert.NotAfter,
            IsValid = isValid,
            Status = isValid ? "Còn hiệu lực" : "Hết hạn"
        };
    }

    private static string GetTypeDisplay(string signatureType) => signatureType switch
    {
        SignatureType.PersonalSignature => "Ký nháy (Lãnh đạo Phòng)",
        SignatureType.LegalSeal => "Ký pháp nhân (Ban Giám hiệu)",
        _ => signatureType
    };
}
