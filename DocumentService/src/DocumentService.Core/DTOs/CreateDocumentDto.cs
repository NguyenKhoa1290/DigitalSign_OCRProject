using System.ComponentModel.DataAnnotations;

namespace DocumentService.Core.DTOs;

public class CreateDocumentDto
{
    [Required]
    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public Guid DocTypeId { get; set; }

    [MaxLength(50)]
    public string? DocNumber { get; set; }

    public DateOnly? IssuedDate { get; set; }

    // MinioPath sẽ được set sau khi upload file qua POST /documents/{id}/upload
}
