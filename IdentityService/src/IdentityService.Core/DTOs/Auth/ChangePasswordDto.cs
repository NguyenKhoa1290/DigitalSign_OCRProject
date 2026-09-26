using System.ComponentModel.DataAnnotations;

namespace IdentityService.Core.DTOs.Auth;

/// <summary>
/// DTO đổi mật khẩu — dùng cho lần đầu đăng nhập và đổi mật khẩu thông thường.
/// Kèm email/SĐT tùy chọn (chỉ cập nhật nếu được cung cấp ở lần đầu đăng nhập).
/// </summary>
public class ChangePasswordDto
{
    /// <summary>Mật khẩu hiện tại (mật khẩu tạm do Admin cấp).</summary>
    [Required(ErrorMessage = "Mật khẩu hiện tại không được để trống.")]
    public string CurrentPassword { get; set; } = string.Empty;

    /// <summary>Mật khẩu mới — tối thiểu 8 ký tự, có chữ hoa, thường và số.</summary>
    [Required(ErrorMessage = "Mật khẩu mới không được để trống.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Mật khẩu mới phải từ 8 đến 128 ký tự.")]
    public string NewPassword { get; set; } = string.Empty;

    /// <summary>Xác nhận mật khẩu mới — phải khớp với NewPassword.</summary>
    [Required(ErrorMessage = "Xác nhận mật khẩu không được để trống.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>Email để khôi phục mật khẩu. Bắt buộc ở lần đăng nhập đầu.</summary>
    [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
    [StringLength(100)]
    public string? Email { get; set; }

    /// <summary>OTP 6 chữ số đã gửi tới Email để xác minh quyền sở hữu.</summary>
    [StringLength(6, MinimumLength = 6, ErrorMessage = "OTP xác minh email phải đúng 6 chữ số.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "OTP xác minh email phải gồm 6 chữ số.")]
    public string? EmailVerificationOtp { get; set; }

    /// <summary>Số điện thoại (tùy chọn).</summary>
    [StringLength(15)]
    public string? PhoneNumber { get; set; }
}
