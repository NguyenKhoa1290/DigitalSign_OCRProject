namespace DocumentService.Core.DTOs;

public class DocumentTypeDto
{
    public Guid Id { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string? Description { get; set; }
}
