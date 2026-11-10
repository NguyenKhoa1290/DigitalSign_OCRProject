using System.ComponentModel.DataAnnotations;

namespace IdentityService.Core.DTOs.Users;

public class ThemePreferenceDto
{
    [Required, RegularExpression("^(light|dark)$", ErrorMessage = "Chỉ chọn giao diện light hoặc dark.")]
    public string Theme { get; set; } = string.Empty;
}
