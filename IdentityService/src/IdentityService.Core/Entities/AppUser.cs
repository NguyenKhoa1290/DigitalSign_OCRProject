namespace IdentityService.Core.Entities;

/// <summary>
/// Đại diện cho người dùng trong hệ thống quản lý tài liệu trường đại học.
/// Maps to table: AppUsers
/// </summary>
public class AppUser
{
    /// <summary>Khóa chính định danh người dùng (GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>Tên đăng nhập duy nhất, tối đa 50 ký tự, không được null.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Mật khẩu đã được hash, không được null.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Họ và tên đầy đủ, tối đa 100 ký tự (hỗ trợ Unicode tiếng Việt).</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Địa chỉ email duy nhất, tối đa 100 ký tự.</summary>
    public string? Email { get; set; }

    /// <summary>Số điện thoại, tối đa 15 ký tự.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Khóa ngoại tham chiếu đến Phòng/Khoa của người dùng.</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>Trạng thái hoạt động của tài khoản. Mặc định: true.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Bắt buộc đổi mật khẩu lần đầu đăng nhập. Admin set = true khi tạo, user đổi xong = false.</summary>
    public bool MustChangePassword { get; set; } = false;

    /// <summary>Thời điểm tạo tài khoản.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // -------------------------
    // Navigation Properties
    // -------------------------

    /// <summary>Phòng/Khoa mà người dùng thuộc về.</summary>
    public virtual Department? Department { get; set; }

    /// <summary>Danh sách vai trò được gán cho người dùng (junction table).</summary>
    public virtual ICollection<AppUserRole> UserRoles { get; set; } = new List<AppUserRole>();
}
