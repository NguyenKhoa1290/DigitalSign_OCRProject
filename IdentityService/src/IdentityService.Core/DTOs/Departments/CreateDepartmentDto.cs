using System.ComponentModel.DataAnnotations;

namespace IdentityService.Core.DTOs.Departments;

/// <summary>
/// DTO nhận thông tin để tạo phòng/khoa mới.
/// </summary>
public class CreateDepartmentDto
{
    /// <summary>Tên đầy đủ của phòng/khoa, hỗ trợ Unicode tiếng Việt, tối đa 150 ký tự.</summary>
    [Required(ErrorMessage = "Tên phòng/khoa không được để trống.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Tên phòng/khoa phải từ 2 đến 150 ký tự.")]
    public string DeptName { get; set; } = string.Empty;

    /// <summary>Mã phòng/khoa duy nhất, tối đa 20 ký tự (ví dụ: CNTT, KTPM, QTKD).</summary>
    [Required(ErrorMessage = "Mã phòng/khoa không được để trống.")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "Mã phòng/khoa phải từ 2 đến 20 ký tự.")]
    [RegularExpression(@"^[A-Z0-9_-]+$", ErrorMessage = "Mã phòng/khoa chỉ được chứa chữ hoa, số, dấu gạch dưới và gạch ngang.")]
    public string DeptCode { get; set; } = string.Empty;

    /// <summary>ID của phòng/khoa cha. Null nếu đây là đơn vị cấp cao nhất.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Mô tả về chức năng, nhiệm vụ của phòng/khoa, tối đa 255 ký tự.</summary>
    [StringLength(255, ErrorMessage = "Mô tả không được vượt quá 255 ký tự.")]
    public string? Description { get; set; }
}
