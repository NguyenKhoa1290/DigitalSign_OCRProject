using System.Text.Json.Serialization;

namespace HauDocumentApp.Models;

public class DocumentTypeDto
{
    public Guid Id { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }

    [JsonIgnore]
    public string Name
    {
        get => TypeName;
        set => TypeName = value;
    }
}
