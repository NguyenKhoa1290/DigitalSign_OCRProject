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
}

