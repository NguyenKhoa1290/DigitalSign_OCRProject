namespace SignService.Infrastructure.Data;

/// <summary>
/// Projection tối thiểu của bảng Documents. Bảng này do DocumentService quản lý,
/// SignService chỉ đọc để resolve MinIO object path.
/// </summary>
public class DocumentFileRecord
{
    public Guid Id { get; set; }
    public string MinioPath { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime? SigningStartedAt { get; set; }
    public Guid ConcurrencyVersion { get; set; }
    public Guid DocTypeId { get; set; }
    public string Status { get; set; } = "";
    public Guid? ProcessingDepartmentId { get; set; }
    public string? ProcessingDepartmentName { get; set; }
    public Guid? AssignedToId { get; set; }
    public string? AssignedToName { get; set; }
    public string? IncomingOriginalPath { get; set; }
    public string? IncomingDirectionPath { get; set; }
    public DateTime? FirstAssignedAt { get; set; }
}

public class IncomingProcessRecord
{
    public Guid Id { get; set; }
    public Guid DocId { get; set; }
    public Guid FromUserId { get; set; }
    public Guid? ToUserId { get; set; }
    public string Action { get; set; } = "";
    public string? Comment { get; set; }
    public DateTime Timestamp { get; set; }
}

public class DocumentTypeRecord
{
    public Guid Id { get; set; }
    public string TypeName { get; set; } = "";
}

