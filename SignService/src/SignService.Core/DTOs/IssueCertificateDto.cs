using System.ComponentModel.DataAnnotations;

namespace SignService.Core.DTOs;

public class IssueCertificateDto
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string FullName { get; set; } = string.Empty;

    public int ValidityYears { get; set; } = 2; // Cert có hiệu lực 2 năm
}
