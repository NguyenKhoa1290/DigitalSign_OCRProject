namespace DocumentService.Core.DTOs;

public class DocumentQueryParams
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }       // tìm theo title, doc_number
    public Guid? DocTypeId { get; set; }
    public string? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Scope { get; set; } // department / school; still constrained by actor access
    [System.Text.Json.Serialization.JsonIgnore] public string? SigningView { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public Guid? SigningUserId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string[] SigningRoles { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public Guid? SigningDepartmentId { get; set; }
}
