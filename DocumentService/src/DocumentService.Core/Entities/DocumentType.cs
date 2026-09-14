namespace DocumentService.Core.Entities;

public class DocumentType
{
    public Guid Id { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Navigation
    public ICollection<Document> Documents { get; set; } = new List<Document>();
}
