namespace IdentityService.Core.Entities;

/// <summary>
/// Đại diện cho Phòng/Khoa trong cơ cấu tổ chức của trường đại học.
/// Hỗ trợ cấu trúc cây phân cấp (self-referencing hierarchy).
/// Maps to table: Departments
/// </summary>
public class Department
{
    /// <summary>Khóa chính định danh phòng/khoa (GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>Tên đầy đủ của phòng/khoa, tối đa 150 ký tự (hỗ trợ Unicode tiếng Việt).</summary>
    public string DeptName { get; set; } = string.Empty;

    /// <summary>Mã phòng/khoa duy nhất, tối đa 20 ký tự, không được null.</summary>
    public string DeptCode { get; set; } = string.Empty;

    /// <summary>Khóa ngoại tự tham chiếu đến phòng/khoa cha (null nếu là gốc).</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Mô tả về phòng/khoa, tối đa 255 ký tự (hỗ trợ Unicode tiếng Việt).</summary>
    public string? Description { get; set; }

    /// <summary>Thời điểm tạo bản ghi.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // -------------------------
    // Navigation Properties
    // -------------------------

    /// <summary>Phòng/Khoa cha trong cấu trúc phân cấp.</summary>
    public virtual Department? Parent { get; set; }

    /// <summary>Danh sách phòng/khoa con trực thuộc.</summary>
    public virtual ICollection<Department> Children { get; set; } = new List<Department>();

    /// <summary>Danh sách người dùng thuộc phòng/khoa này.</summary>
    public virtual ICollection<AppUser> Users { get; set; } = new List<AppUser>();
}
