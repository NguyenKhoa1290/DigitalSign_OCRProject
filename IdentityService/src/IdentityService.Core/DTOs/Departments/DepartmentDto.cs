namespace IdentityService.Core.DTOs.Departments;

/// <summary>
/// DTO đại diện cho thông tin phòng/khoa, hỗ trợ cấu trúc cây phân cấp.
/// </summary>
public class DepartmentDto
{
    /// <summary>ID (GUID) của phòng/khoa.</summary>
    public Guid Id { get; set; }

    /// <summary>Tên đầy đủ của phòng/khoa.</summary>
    public string DeptName { get; set; } = string.Empty;

    /// <summary>Mã phòng/khoa duy nhất.</summary>
    public string DeptCode { get; set; } = string.Empty;

    /// <summary>ID của phòng/khoa cha. Null nếu là đơn vị gốc.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Tên phòng/khoa cha. Null nếu là đơn vị gốc.</summary>
    public string? ParentName { get; set; }

    /// <summary>Mô tả về phòng/khoa.</summary>
    public string? Description { get; set; }

    /// <summary>Thời điểm tạo bản ghi (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Danh sách phòng/khoa con trực thuộc (đệ quy).
    /// Được dùng khi trả về cấu trúc cây đầy đủ.
    /// </summary>
    public List<DepartmentDto> Children { get; set; } = new List<DepartmentDto>();
}
