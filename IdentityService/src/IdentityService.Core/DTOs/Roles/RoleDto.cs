namespace IdentityService.Core.DTOs.Roles;

/// <summary>
/// DTO đại diện cho thông tin vai trò trong hệ thống.
/// </summary>
public class RoleDto
{
    /// <summary>ID (GUID) của vai trò.</summary>
    public Guid Id { get; set; }

    /// <summary>Tên vai trò duy nhất.</summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>Mô tả về vai trò và quyền hạn tương ứng.</summary>
    public string? Description { get; set; }
}
