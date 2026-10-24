using System.ComponentModel.DataAnnotations;

namespace DocumentService.Core.DTOs;

public class DocumentPurgeDto
{
    [Required, MaxLength(1000)] public string Reason { get; set; } = "";
    [Required, MaxLength(500)] public string ConfirmationTitle { get; set; } = "";
    [Required] public Guid? ExpectedVersion { get; set; }
}

public sealed record DocumentPurgeResult(Guid DocId, bool FilesPendingCleanup);
