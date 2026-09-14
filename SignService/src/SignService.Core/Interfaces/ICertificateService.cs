using SignService.Core.Entities;

namespace SignService.Core.Interfaces;

public interface ICertificateService
{
    // Tạo Root CA của trường (gọi một lần khi khởi tạo)
    Task InitializeRootCaAsync();

    // Cấp certificate cho user (PKI nội bộ)
    Task<UserCertificate> IssueCertificateAsync(Guid userId, string username, string fullName, int validityYears = 2);

    // Lấy certificate của user
    Task<UserCertificate?> GetUserCertificateAsync(Guid userId);

    // Kiểm tra certificate còn hạn không
    bool IsCertificateValid(UserCertificate cert);
}
