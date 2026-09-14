using System.ComponentModel.DataAnnotations;

namespace IdentityService.Core.DTOs.Users;

/// <summary>
/// DTO nhận thông tin để tạo người dùng mới.
/// </summary>
public class CreateUserDto
{
    /// <summary>Tên đăng nhập duy nhất, từ 3 đến 50 ký tự.</summary>
    [Required(ErrorMessage = "Tên đăng nhập không được để trống.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Tên đăng nhập phải từ 3 đến 50 ký tự.")]
    [RegularExpression(@"^[a-zA-Z0-9_.-]+$", ErrorMessage = "Tên đăng nhập chỉ được chứa chữ cái, số, dấu gạch dưới, dấu chấm và dấu gạch ngang.")]
    public string Username { get; set; } = string.Empty;

    /// <summary>Mật khẩu plain-text, sẽ được hash trước khi lưu.</summary>
    [Required(ErrorMessage = "Mật khẩu không được để trống.")]
    [StringLength(128, MinimumLength = 6, ErrorMessage = "Mật khẩu phải từ 6 đến 128 ký tự.")]
    public string Password { get; set; } = string.Empty;

    /// <summary>Họ và tên đầy đủ, hỗ trợ Unicode tiếng Việt, tối đa 100 ký tự.</summary>
    [Required(ErrorMessage = "Họ và tên không được để trống.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ và tên phải từ 2 đến 100 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Địa chỉ email hợp lệ, tối đa 100 ký tự.</summary>
    [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
    [StringLength(100, ErrorMessage = "Email không được vượt quá 100 ký tự.")]
    public string? Email { get; set; }

    /// <summary>Số điện thoại, tối đa 15 ký tự.</summary>
    [StringLength(15, ErrorMessage = "Số điện thoại không được vượt quá 15 ký tự.")]
    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    public string? PhoneNumber { get; set; }

    /// <summary>ID của phòng/khoa mà người dùng thuộc về. Null nếu chưa xác định.</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>Danh sách ID các vai trò cần gán cho người dùng mới.</summary>
    public List<Guid> RoleIds { get; set; } = new List<Guid>();
}
