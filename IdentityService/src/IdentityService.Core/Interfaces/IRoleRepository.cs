using IdentityService.Core.Entities;

namespace IdentityService.Core.Interfaces;

/// <summary>
/// Giao diện repository cho thao tác dữ liệu vai trò (AppRole).
/// Được triển khai tại tầng Infrastructure.
/// </summary>
public interface IRoleRepository
{
    /// <summary>Lấy toàn bộ danh sách vai trò.</summary>
    /// <returns>Danh sách tất cả AppRole trong hệ thống.</returns>
    Task<IEnumerable<AppRole>> GetAllAsync();

    /// <summary>Tìm vai trò theo ID.</summary>
    /// <param name="id">GUID định danh vai trò.</param>
    /// <returns>AppRole nếu tồn tại, null nếu không tìm thấy.</returns>
    Task<AppRole?> GetByIdAsync(Guid id);

    /// <summary>Tìm vai trò theo tên vai trò.</summary>
    /// <param name="roleName">Tên vai trò (không phân biệt hoa thường).</param>
    /// <returns>AppRole nếu tồn tại, null nếu không tìm thấy.</returns>
    Task<AppRole?> GetByNameAsync(string roleName);

    /// <summary>Tạo vai trò mới.</summary>
    /// <param name="role">Đối tượng AppRole cần tạo.</param>
    /// <returns>AppRole vừa được tạo (kèm Id được gán).</returns>
    Task<AppRole> CreateAsync(AppRole role);

    /// <summary>Cập nhật thông tin vai trò.</summary>
    /// <param name="role">Đối tượng AppRole với thông tin đã cập nhật.</param>
    /// <returns>AppRole sau khi cập nhật.</returns>
    Task<AppRole> UpdateAsync(AppRole role);

    /// <summary>Xóa vai trò theo ID.</summary>
    /// <param name="id">GUID định danh vai trò cần xóa.</param>
    /// <returns>True nếu xóa thành công, False nếu không tìm thấy.</returns>
    Task<bool> DeleteAsync(Guid id);
}
