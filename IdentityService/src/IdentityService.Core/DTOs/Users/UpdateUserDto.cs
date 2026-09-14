using System.ComponentModel.DataAnnotations;

namespace IdentityService.Core.DTOs.Users;

/// <summary>
/// DTO nhận thông tin để cập nhật người dùng hiện có.
/// Không cho phép thay đổi username và password thông qua endpoint này.
/// </summary>
public class UpdateUserDto
{
    /// <summary>Họ và tên đầy đủ mới, hỗ trợ Unicode tiếng Việt, tối đa 100 ký tự.</summary>
    [Required(ErrorMessage = "Họ và tên không được để trống.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ và tên phải từ 2 đến 100 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Địa chỉ email mới, hợp lệ và tối đa 100 ký tự.</summary>
    [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
    [StringLength(100, ErrorMessage = "Email không được vượt quá 100 ký tự.")]
    public string? Email { get; set; }

    /// <summary>Số điện thoại mới, tối đa 15 ký tự.</summary>
    [StringLength(15, ErrorMessage = "Số điện thoại không được vượt quá 15 ký tự.")]
    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    public string? PhoneNumber { get; set; }

    /// <summary>ID của phòng/khoa mới. Null để xóa liên kết phòng/khoa.</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>Trạng thái hoạt động của tài khoản (kích hoạt hoặc khóa).</summary>
    public bool IsActive { get; set; } = true;
}
