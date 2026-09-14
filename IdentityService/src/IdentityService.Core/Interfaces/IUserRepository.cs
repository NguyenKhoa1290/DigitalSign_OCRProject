using IdentityService.Core.Entities;

namespace IdentityService.Core.Interfaces;

/// <summary>
/// Giao diện repository cho thao tác dữ liệu người dùng (AppUser).
/// Được triển khai tại tầng Infrastructure.
/// </summary>
public interface IUserRepository
{
    /// <summary>Tìm người dùng theo tên đăng nhập.</summary>
    /// <param name="username">Tên đăng nhập (không phân biệt hoa thường).</param>
    /// <returns>AppUser nếu tồn tại, null nếu không tìm thấy.</returns>
    Task<AppUser?> GetByUsernameAsync(string username);

    /// <summary>Tìm người dùng theo địa chỉ email.</summary>
    /// <param name="email">Địa chỉ email.</param>
    /// <returns>AppUser nếu tồn tại, null nếu không tìm thấy.</returns>
    Task<AppUser?> GetByEmailAsync(string email);

    /// <summary>Tìm người dùng theo ID.</summary>
    /// <param name="id">GUID định danh người dùng.</param>
    /// <returns>AppUser nếu tồn tại, null nếu không tìm thấy.</returns>
    Task<AppUser?> GetByIdAsync(Guid id);

    /// <summary>Lấy danh sách người dùng có phân trang và tìm kiếm.</summary>
    /// <param name="page">Số trang (bắt đầu từ 1).</param>
    /// <param name="pageSize">Số bản ghi mỗi trang.</param>
    /// <param name="search">Từ khóa tìm kiếm (tên, username, email). Null = lấy tất cả.</param>
    /// <returns>Tuple gồm danh sách người dùng và tổng số bản ghi.</returns>
    Task<(IEnumerable<AppUser> Users, int Total)> GetAllAsync(int page, int pageSize, string? search);

    /// <summary>Tạo người dùng mới.</summary>
    /// <param name="user">Đối tượng AppUser cần tạo.</param>
    /// <returns>AppUser vừa được tạo (kèm Id được gán).</returns>
    Task<AppUser> CreateAsync(AppUser user);

    /// <summary>Cập nhật thông tin người dùng.</summary>
    /// <param name="user">Đối tượng AppUser với thông tin đã được cập nhật.</param>
    /// <returns>AppUser sau khi cập nhật.</returns>
    Task<AppUser> UpdateAsync(AppUser user);

    /// <summary>Xóa người dùng theo ID (xóa mềm hoặc cứng tùy implementation).</summary>
    /// <param name="id">GUID định danh người dùng cần xóa.</param>
    /// <returns>True nếu xóa thành công, False nếu không tìm thấy.</returns>
    Task<bool> DeleteAsync(Guid id);

    /// <summary>Gán vai trò cho người dùng.</summary>
    /// <param name="userId">GUID của người dùng.</param>
    /// <param name="roleId">GUID của vai trò cần gán.</param>
    /// <returns>True nếu gán thành công.</returns>
    Task<bool> AssignRoleAsync(Guid userId, Guid roleId);

    /// <summary>Xóa vai trò khỏi người dùng.</summary>
    /// <param name="userId">GUID của người dùng.</param>
    /// <param name="roleId">GUID của vai trò cần xóa.</param>
    /// <returns>True nếu xóa thành công.</returns>
    Task<bool> RemoveRoleAsync(Guid userId, Guid roleId);

    /// <summary>Lấy danh sách tên vai trò của người dùng.</summary>
    /// <param name="userId">GUID của người dùng.</param>
    /// <returns>Danh sách tên vai trò (RoleName).</returns>
    Task<IEnumerable<string>> GetUserRolesAsync(Guid userId);
}
