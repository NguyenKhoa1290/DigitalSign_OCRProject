namespace HauDocumentApp.Models;

public class CreateDocumentDto
{
    public string Title { get; set; } = string.Empty;
    public string? DocumentNumber { get; set; }
    public Guid? DocumentTypeId { get; set; }
    public DateTime? IssuedDate { get; set; }
    public Guid? DepartmentId { get; set; }
}

public class UpdateDocumentDto
{
    public string Title { get; set; } = string.Empty;
    public string? DocumentNumber { get; set; }
    public Guid? DocumentTypeId { get; set; }
    public DateTime? IssuedDate { get; set; }
}

public class DocumentActionDto
{
    public string? Comment { get; set; }
    public Guid? AssignedToId { get; set; }
}
