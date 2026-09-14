namespace IdentityService.Core.Entities;

/// <summary>
/// Đại diện cho vai trò (role) trong hệ thống phân quyền.
/// Maps to table: AppRoles
/// </summary>
public class AppRole
{
    /// <summary>Khóa chính định danh vai trò (GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>Tên vai trò duy nhất, tối đa 50 ký tự, không được null.</summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>Mô tả về vai trò, tối đa 255 ký tự (hỗ trợ Unicode tiếng Việt).</summary>
    public string? Description { get; set; }

    // -------------------------
    // Navigation Properties
    // -------------------------

    /// <summary>Danh sách người dùng được gán vai trò này (junction table).</summary>
    public virtual ICollection<AppUserRole> UserRoles { get; set; } = new List<AppUserRole>();
}
