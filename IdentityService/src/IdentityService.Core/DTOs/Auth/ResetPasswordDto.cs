using System.ComponentModel.DataAnnotations;

namespace IdentityService.Core.DTOs.Auth;

/// <summary>Đặt lại mật khẩu bằng OTP nhận qua email.</summary>
public class ResetPasswordDto
{
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>OTP 6 chữ số nhận qua email.</summary>
    [Required(ErrorMessage = "Mã OTP không được để trống.")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "OTP phải đúng 6 chữ số.")]
    public string Otp { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu mới không được để trống.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Mật khẩu mới phải từ 8 đến 128 ký tự.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Xác nhận mật khẩu không được để trống.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
