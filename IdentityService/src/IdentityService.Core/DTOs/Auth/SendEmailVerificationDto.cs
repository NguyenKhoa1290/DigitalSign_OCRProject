using System.ComponentModel.DataAnnotations;

namespace IdentityService.Core.DTOs.Auth;

public class SendEmailVerificationDto
{
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
    [StringLength(100, ErrorMessage = "Email không được vượt quá 100 ký tự.")]
    public string Email { get; set; } = string.Empty;
}
