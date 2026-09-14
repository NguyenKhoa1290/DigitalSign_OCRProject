using System.ComponentModel.DataAnnotations;

namespace IdentityService.Core.DTOs.Auth;

/// <summary>
/// DTO nhận yêu cầu gia hạn Access Token bằng Refresh Token.
/// </summary>
public class RefreshTokenRequestDto
{
    /// <summary>Access Token hiện tại (có thể đã hết hạn).</summary>
    [Required(ErrorMessage = "Access Token không được để trống.")]
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Refresh Token hợp lệ tương ứng với Access Token.</summary>
    [Required(ErrorMessage = "Refresh Token không được để trống.")]
    public string RefreshToken { get; set; } = string.Empty;
}
