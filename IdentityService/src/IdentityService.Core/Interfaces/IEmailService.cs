namespace IdentityService.Core.Interfaces;

/// <summary>
/// Dịch vụ gửi email transactional (OTP khôi phục mật khẩu, thông báo...).
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Gửi email chứa OTP 6 chữ số để khôi phục mật khẩu.
    /// </summary>
    /// <param name="toEmail">Địa chỉ email người nhận.</param>
    /// <param name="toName">Tên hiển thị người nhận.</param>
    /// <param name="otp">OTP 6 chữ số plain-text (chưa hash).</param>
    Task SendPasswordResetEmailAsync(string toEmail, string toName, string otp);
}
