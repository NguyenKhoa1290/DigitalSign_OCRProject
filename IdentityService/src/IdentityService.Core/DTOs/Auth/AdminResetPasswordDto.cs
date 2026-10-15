using System.ComponentModel.DataAnnotations;

namespace IdentityService.Core.DTOs.Auth;

/// <summary>Mật khẩu tạm do Admin cấp khi đặt lại tài khoản.</summary>
public class AdminResetPasswordDto
{
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu tạm.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Mật khẩu tạm phải từ 8 đến 128 ký tự.")]
    public string TemporaryPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu tạm.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
