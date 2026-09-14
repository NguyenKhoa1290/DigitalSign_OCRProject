using SignService.Core.DTOs;

namespace SignService.Core.Interfaces;

public interface ISignService
{
    // Ký nháy (Lãnh đạo Phòng)
    Task<SignResultDto> PersonalSignAsync(SignRequestDto request);

    // Ký pháp nhân (Ban Giám hiệu)
    Task<SignResultDto> LegalSealAsync(SignRequestDto request);

    // Xác minh chữ ký
    Task<VerifyResultDto> VerifySignaturesAsync(Guid docId);

    // Lấy danh sách chữ ký của văn bản
    Task<IEnumerable<SignatureDto>> GetSignaturesByDocumentAsync(Guid docId);

    // Cấp certificate cho user
    Task<CertificateDto> IssueCertificateAsync(IssueCertificateDto request);

    // Lấy thông tin certificate của user
    Task<CertificateDto?> GetCertificateAsync(Guid userId);
}
