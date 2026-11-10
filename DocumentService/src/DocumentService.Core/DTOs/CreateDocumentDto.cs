using System.ComponentModel.DataAnnotations;

namespace DocumentService.Core.DTOs;

public class CreateDocumentDto
{
    // Set exclusively by the authenticated API scope filter; never accept these from JSON.
    [System.Text.Json.Serialization.JsonIgnore] public Guid? DepartmentId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? DepartmentName { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool IsSchoolWide { get; set; }
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
