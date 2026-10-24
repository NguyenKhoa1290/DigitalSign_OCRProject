using System.ComponentModel.DataAnnotations;

namespace DocumentService.Core.DTOs;

public class CancelDocumentDto
{
    [Required, MaxLength(1000)] public string Reason { get; set; } = "";
    // Legacy documents legitimately have Guid.Empty. Null means the client omitted the version.
    [Required] public Guid? ExpectedVersion { get; set; }
}
