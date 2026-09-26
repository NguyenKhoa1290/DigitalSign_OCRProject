using IdentityService.Core.DTOs.Auth;

namespace IdentityService.Core.Services;

/// <summary>
/// Giao diện dịch vụ xác thực người dùng (Authentication Service).
/// Xử lý: đăng nhập, làm mới token, xác thực token, đăng xuất,
/// đổi mật khẩu lần đầu, quên mật khẩu và đặt lại mật khẩu.
/// </summary>
public interface IAuthService
{
    /// <summary>Xác thực thông tin đăng nhập và cấp JWT + Refresh Token.</summary>
    Task<LoginResponseDto> LoginAsync(LoginRequestDto request);

    /// <summary>Gia hạn Access Token bằng Refresh Token hợp lệ.</summary>
    Task<LoginResponseDto> RefreshTokenAsync(RefreshTokenRequestDto request);

    /// <summary>Xác thực tính hợp lệ của Access Token (dùng bởi API Gateway).</summary>
    Task<ValidateTokenResponseDto> ValidateTokenAsync(ValidateTokenRequestDto request);

    /// <summary>Đăng xuất — thu hồi phiên làm việc.</summary>
    Task LogoutAsync(string accessToken);

    /// <summary>Gửi OTP xác minh tới email do người dùng đang đăng nhập cung cấp.</summary>
    Task SendEmailVerificationAsync(Guid userId, SendEmailVerificationDto dto);

    /// <summary>
    /// Đổi mật khẩu (dùng cho lần đầu đăng nhập lẫn đổi thông thường).
    /// Nếu MustChangePassword = true → đặt về false sau khi đổi thành công.
    /// Email là bắt buộc và phải có OTP hợp lệ nếu MustChangePassword = true.
    /// </summary>
    Task ChangePasswordAsync(Guid userId, ChangePasswordDto dto);

    /// <summary>
    /// Gửi OTP 6 chữ số về email để khôi phục mật khẩu.
    /// Không báo lỗi nếu email không tồn tại (tránh user enumeration).
    /// </summary>
    Task ForgotPasswordAsync(ForgotPasswordDto dto);

    /// <summary>
    /// Xác thực OTP và đặt lại mật khẩu mới.
    /// OTP hết hạn sau 15 phút hoặc sau khi dùng 1 lần.
    /// </summary>
    Task ResetPasswordAsync(ResetPasswordDto dto);
}
