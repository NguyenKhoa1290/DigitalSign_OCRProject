using System.ComponentModel.DataAnnotations;

namespace IdentityService.Core.DTOs.Auth;

/// <summary>
/// DTO nhận yêu cầu xác thực token từ các service khác trong hệ thống.
/// </summary>
public class ValidateTokenRequestDto
{
    /// <summary>JWT Access Token cần được xác thực.</summary>
    [Required(ErrorMessage = "Token không được để trống.")]
    public string Token { get; set; } = string.Empty;
}
