using IdentityService.Core.Entities;

namespace IdentityService.Core.Interfaces;

/// <summary>
/// Repository lưu trữ và tra cứu PasswordResetToken (OTP khôi phục mật khẩu).
/// </summary>
public interface IPasswordResetRepository
{
    /// <summary>Tạo token mới và lưu vào DB.</summary>
    Task<PasswordResetToken> CreateAsync(PasswordResetToken token);

    /// <summary>
    /// Tìm token hợp lệ (chưa dùng, chưa hết hạn) của user với hash cho trước.
    /// </summary>
    Task<PasswordResetToken?> GetValidTokenAsync(Guid userId, string tokenHash);

    /// <summary>Vô hiệu hoá (IsUsed = true) tất cả token của user — gọi sau khi reset thành công.</summary>
    Task InvalidateAllAsync(Guid userId);
}
