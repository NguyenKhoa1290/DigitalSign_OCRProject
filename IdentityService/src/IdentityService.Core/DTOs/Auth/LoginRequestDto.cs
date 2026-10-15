using System.ComponentModel.DataAnnotations;

namespace IdentityService.Core.DTOs.Auth;

/// <summary>
/// DTO nhận thông tin đăng nhập từ client.
/// </summary>
public class LoginRequestDto
{
    /// <summary>Mã cán bộ/giảng viên hoặc email đã xác thực.</summary>
    [Required(ErrorMessage = "Vui lòng nhập mã cán bộ/giảng viên hoặc email.")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Thông tin đăng nhập phải từ 3 đến 100 ký tự.")]
    public string Username { get; set; } = string.Empty;

    /// <summary>Mật khẩu của người dùng (plain-text, sẽ được hash khi xử lý).</summary>
    [Required(ErrorMessage = "Mật khẩu không được để trống.")]
    [StringLength(128, MinimumLength = 6, ErrorMessage = "Mật khẩu phải từ 6 đến 128 ký tự.")]
    public string Password { get; set; } = string.Empty;
}
