using SignService.Core.Entities;

namespace SignService.Core.Interfaces;

public interface ICertificateService
{
    // Tạo Root CA của trường (gọi một lần khi khởi tạo)
    Task InitializeRootCaAsync();

    // Cấp certificate cho user (PKI nội bộ)
    Task<UserCertificate> IssueCertificateAsync(
        Guid userId,
        string username,
        string fullName,
        string certificateType = "Personal",
        int validityDays = 365);

    // Lấy certificate của user
    Task<UserCertificate?> GetUserCertificateAsync(Guid userId);

    // Lấy toàn bộ certificate đang lưu trong kho nội bộ
    Task<IReadOnlyList<UserCertificate>> GetAllCertificatesAsync();

    // Thu hồi certificate bằng cách xóa PFX của user
    Task<bool> RevokeCertificateAsync(Guid userId);

    // Kiểm tra certificate còn hạn không
    bool IsCertificateValid(UserCertificate cert);
}
