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

    [Required]
    [RegularExpression("Personal|Organization")]
    public string CertificateType { get; set; } = "Personal";

    [Range(1, 3650)]
    public int ValidityDays { get; set; } = 365;
}

public class IssueOwnCertificateDto
{
    [Range(1, 3650)]
    public int ValidityDays { get; set; } = 365;
}
