using System.ComponentModel.DataAnnotations;

namespace DocumentService.Core.DTOs;

public class DocumentDeletionDto
{
    [MaxLength(1000)]
    public string? Reason { get; set; }
}
