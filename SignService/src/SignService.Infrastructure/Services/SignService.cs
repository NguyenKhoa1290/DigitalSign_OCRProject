using Microsoft.Extensions.Configuration;
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
    private readonly IDocumentFileRepository _documentFileRepository;
    private readonly ILogger<SignService> _logger;
    private readonly string _minioBucket;
    private readonly ISigningDocumentPreparer? _preparer;

    public SignService(
        ISignatureRepository signatureRepo,
        ICertificateService certService,
        IPdfSigningService pdfSigningService,
        IMinioService minioService,
        IDocumentFileRepository documentFileRepository,
        IConfiguration config,
        ILogger<SignService> logger, ISigningDocumentPreparer? preparer = null)
    {
        _signatureRepo = signatureRepo;
        _certService = certService;
        _pdfSigningService = pdfSigningService;
        _minioService = minioService;
        _documentFileRepository = documentFileRepository;
        _logger = logger;
        _preparer = preparer;
        _minioBucket = config["MinioSettings:Bucket"] ?? "documents";
    }

    public async Task<SignResultDto> PersonalSignAsync(SignRequestDto request)
    {
        return await SignInternalAsync(request, SignatureType.PersonalSignature, requireExisting: null);
    }

    public async Task<SignResultDto> LegalSealAsync(SignRequestDto request)
    {
        if (await _documentFileRepository.IsIncomingAsync(request.DocId))
            throw new IncomingConflictException("Công văn đến dùng ký chỉ đạo và ký phân công, không dùng ký pháp nhân của chiều đi.");
        // Yêu cầu phải có PersonalSignature trước
        bool hasPersonal = await _signatureRepo.HasSignatureAsync(request.DocId, SignatureType.PersonalSignature);
        if (!hasPersonal)
            throw new SignServiceException("Văn bản chưa có chữ ký nháy. Lãnh đạo Phòng cần ký trước khi ký pháp nhân.");

        return await SignInternalAsync(request, SignatureType.LegalSeal, requireExisting: null);
    }

    private async Task<SignResultDto> SignInternalAsync(SignRequestDto request, string signatureType, string? requireExisting)
    {
        if (await _documentFileRepository.IsIncomingAsync(request.DocId))
            throw new IncomingConflictException("Công văn đến dùng ký chỉ đạo và ký phân công, không dùng ký nháy của chiều đi.");
        _logger.LogInformation("Bắt đầu ký {Type} cho văn bản {DocId} bởi {SignerId}",
            signatureType, request.DocId, request.SignerId);

        // Kiểm tra cert của người ký
        var userCert = await _certService.GetUserCertificateAsync(request.SignerId)
                       ?? throw new CertificateNotFoundException(request.SignerId);

        if (!_certService.IsCertificateValid(userCert))
            throw new CertificateExpiredException();

        // Reject unsupported input before persisting a signing-start marker.
        if (_preparer == null) await ResolveDocumentObjectNameAsync(request.DocId);
        await using var signing = await _documentFileRepository.BeginSigningAsync(request.DocId);
        await _documentFileRepository.ValidateSigningStageAsync(request.DocId, signatureType);

        // Kiểm tra đã ký chưa
        bool alreadySigned = await _signatureRepo.HasSignatureAsync(request.DocId, signatureType);
        if (alreadySigned)
            throw new AlreadySignedException(signatureType);

        // Tải PDF từ MinIO theo đường dẫn thật DocumentService đã lưu.
        string objectName = $"{request.DocId}/signed-{Guid.NewGuid():N}.pdf";
        byte[] pdfBytes;
        try
        {
            pdfBytes = _preparer != null ? await _preparer.PrepareAsync(request.DocId)
                : await _minioService.DownloadFileAsync(await ResolveDocumentObjectNameAsync(request.DocId));
        }
        catch (SignServiceException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không tìm thấy PDF trên MinIO: {ObjectName}", objectName);
            throw new DocumentNotFoundException(request.DocId);
        }

        if (signatureType == SignatureType.LegalSeal)
        {
            var previous = await _pdfSigningService.VerifyPdfSignaturesAsync(pdfBytes);
            if (!previous.IsValid || !previous.Signatures.Any(s => s.SignatureType == SignatureType.PersonalSignature && s.IsIntact))
                throw new IncomingConflictException("PDF thiếu chữ ký nháy hợp lệ hoặc đã bị thay đổi. Chưa ký pháp nhân.");
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

        var verified = await _pdfSigningService.VerifyPdfSignaturesAsync(signedPdf);
        if (!verified.IsValid || !verified.Signatures.Any(s => s.SignatureType == signatureType && s.IsIntact))
            throw new PdfSigningException("Không xác minh được PDF sau khi ký.");
        // Write a new immutable object, preserving source and earlier signatures.
        await _minioService.UploadFileAsync(objectName, signedPdf, "application/pdf");
        await _documentFileRepository.SaveSignedPdfAsync(request.DocId, objectName);

        // Lưu DB
        var signature = await _signatureRepo.CreateAsync(new Signature
        {
            DocId = request.DocId,
            SignerId = request.SignerId,
            SignatureType = signatureType,
            SignedFilePath = objectName,
            SignedAt = DateTime.UtcNow
        });
        await signing.CompleteAsync();

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

        string objectName = await ResolveDocumentObjectNameAsync(docId);
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
            IsIntact = s.IsIntact, IsCertificateValid = s.IsCertificateValid,
            IsCertificateTrusted = s.IsCertificateTrusted, IsRevoked = s.IsRevoked
        }).ToList();

        return new VerifyResultDto
        {
            DocId = docId,
            IsValid = pdfVerifyResult.IsValid,
            CoversWholeDocument = pdfVerifyResult.CoversWholeDocument, Message = pdfVerifyResult.Message,
            TotalSignatures = pdfVerifyResult.SignatureCount,
            HasPersonalSignature = hasPersonal,
            HasLegalSeal = hasLegalSeal,
            HasIncomingDirectionSignature = dbSignatures.Any(s => s.SignatureType == SignatureType.IncomingDirection),
            HasIncomingAssignmentSignature = dbSignatures.Any(s => s.SignatureType == SignatureType.IncomingAssignment),
            Signatures = verifyInfoList
        };
    }

    public async Task<IEnumerable<SignatureDto>> GetSignaturesByDocumentAsync(Guid docId)
    {
        var objectName = await ResolveDocumentObjectNameAsync(docId);
        var signatures = (await _signatureRepo.GetByDocumentIdAsync(docId)).ToArray();
        if (signatures.Length == 0) return Array.Empty<SignatureDto>();

        var embeddedNames = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var pdf = await _minioService.DownloadFileAsync(objectName);
            var evidence = await _pdfSigningService.VerifyPdfSignaturesAsync(pdf);
            // Each workflow step has one signature. Use its embedded certificate
            // name even after expiry/revocation or removal of the server PFX.
            foreach (var group in evidence.Signatures.Where(s => s.IsIntact
                         && !string.IsNullOrWhiteSpace(s.SignerName)
                         && s.SignerName != "Unknown").GroupBy(s => s.SignatureType))
                if (group.Count() == 1) embeddedNames[group.Key] = group.Single().SignerName;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đọc được tên người ký trong PDF của {DocId}", docId);
        }

        var certificateNames = new Dictionary<Guid, string>();
        foreach (var signerId in signatures.Where(s => !embeddedNames.ContainsKey(s.SignatureType))
                     .Select(s => s.SignerId).Distinct())
        {
            try
            {
                certificateNames[signerId] = (await _certService.GetUserCertificateAsync(signerId))?.FullName ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không đọc được tên trên chứng thư của {SignerId}", signerId);
            }
        }

        return signatures.Select(s => new SignatureDto
        {
            Id = s.Id,
            DocId = s.DocId,
            SignerId = s.SignerId,
            SignerName = embeddedNames.GetValueOrDefault(s.SignatureType)
                ?? certificateNames.GetValueOrDefault(s.SignerId, string.Empty),
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
            request.CertificateType,
            request.ValidityDays);

        return ToCertificateDto(cert);
    }

    public async Task<IEnumerable<CertificateDto>> GetCertificatesAsync()
    {
        var certificates = await _certService.GetAllCertificatesAsync();
        return certificates.Select(ToCertificateDto);
    }

    public Task<bool> RevokeCertificateAsync(Guid userId)
        => _certService.RevokeCertificateAsync(userId);

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
            CertificateType = cert.CertificateType,
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
        SignatureType.IncomingDirection => "Ký chỉ đạo (Ban Giám hiệu)",
        SignatureType.IncomingAssignment => "Ký phân công (Lãnh đạo phòng)",
        _ => signatureType
    };

    private async Task<string> ResolveDocumentObjectNameAsync(Guid docId)
    {
        var minioPath = await _documentFileRepository.GetMinioPathAsync(docId);
        if (string.IsNullOrWhiteSpace(minioPath))
        {
            _logger.LogWarning("Không tìm thấy MinioPath của văn bản {DocId}", docId);
            throw new DocumentNotFoundException(docId);
        }

        var objectName = NormalizeObjectName(minioPath);
        if (string.IsNullOrWhiteSpace(objectName))
        {
            _logger.LogWarning("MinioPath của văn bản {DocId} không hợp lệ: {MinioPath}", docId, minioPath);
            throw new DocumentNotFoundException(docId);
        }

        if (!objectName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new SignServiceException("Cần xuất Word hoặc ảnh thành PDF và tải lại trước khi ký số hoặc xác minh chữ ký.");

        _logger.LogInformation("Resolved MinIO object cho văn bản {DocId}: {MinioPath} -> {ObjectName}",
            docId, minioPath, objectName);

        return objectName;
    }

    private string NormalizeObjectName(string minioPath)
    {
        var path = minioPath.Trim().Replace('\\', '/');

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
            path = uri.AbsolutePath.TrimStart('/');

        path = path.TrimStart('/');

        var bucketPrefix = $"{_minioBucket.Trim('/')}/";
        if (path.StartsWith(bucketPrefix, StringComparison.OrdinalIgnoreCase))
            path = path[bucketPrefix.Length..];

        return path;
    }
}
