namespace IdentityService.Core.DTOs.Users;

/// <summary>
/// DTO trả về thông tin người dùng (không bao gồm thông tin nhạy cảm như password hash).
/// </summary>
public class UserDto
{
    /// <summary>ID (GUID) của người dùng.</summary>
    public Guid Id { get; set; }

    /// <summary>Tên đăng nhập.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Họ và tên đầy đủ.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Địa chỉ email.</summary>
    public string? Email { get; set; }

    /// <summary>Email hiện tại đã được xác minh hay chưa.</summary>
    public bool IsEmailVerified { get; set; }

    /// <summary>Số điện thoại.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>ID của phòng/khoa.</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>Tên phòng/khoa mà người dùng thuộc về.</summary>
    public string? DepartmentName { get; set; }

    /// <summary>Trạng thái hoạt động của tài khoản.</summary>
    public bool IsActive { get; set; }

    /// <summary>Thời điểm tạo tài khoản (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Danh sách tên vai trò được gán cho người dùng.</summary>
    public List<string> Roles { get; set; } = new List<string>();
}
