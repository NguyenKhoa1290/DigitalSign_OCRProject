using System.ComponentModel.DataAnnotations;

namespace DocumentService.Core.DTOs;

public class RejectDocumentDto
{
    [Required]
    public string Reason { get; set; } = string.Empty;
}
