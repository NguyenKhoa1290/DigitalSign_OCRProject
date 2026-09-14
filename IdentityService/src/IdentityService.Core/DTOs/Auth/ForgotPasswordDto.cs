using System.ComponentModel.DataAnnotations;

namespace IdentityService.Core.DTOs.Auth;

/// <summary>Yêu cầu gửi OTP khôi phục mật khẩu qua email.</summary>
public class ForgotPasswordDto
{
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = string.Empty;
}
