using IdentityService.Core.DTOs.Roles;

namespace IdentityService.Core.Services;

/// <summary>
/// Giao diện dịch vụ quản lý vai trò (Role Management Service).
/// </summary>
public interface IRoleService
{
    /// <summary>
    /// Lấy danh sách tất cả vai trò trong hệ thống.
    /// </summary>
    /// <returns>Danh sách RoleDto.</returns>
    Task<IEnumerable<RoleDto>> GetAllRolesAsync();

    /// <summary>
    /// Lấy thông tin chi tiết vai trò theo ID.
    /// </summary>
    /// <param name="id">GUID định danh vai trò.</param>
    /// <returns>RoleDto nếu tìm thấy.</returns>
    /// <exception cref="Exceptions.RoleNotFoundException">Khi không tìm thấy vai trò.</exception>
    Task<RoleDto> GetRoleByIdAsync(Guid id);

    /// <summary>
    /// Lấy thông tin vai trò theo tên vai trò.
    /// </summary>
    /// <param name="roleName">Tên vai trò.</param>
    /// <returns>RoleDto nếu tìm thấy.</returns>
    /// <exception cref="Exceptions.RoleNotFoundException">Khi không tìm thấy vai trò.</exception>
    Task<RoleDto> GetRoleByNameAsync(string roleName);
}
