using IdentityService.Core.Common;
using IdentityService.Core.DTOs.Users;

namespace IdentityService.Core.Services;

/// <summary>
/// Giao diện dịch vụ quản lý người dùng.
/// Cung cấp các thao tác CRUD với phân trang và quản lý vai trò.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Lấy danh sách người dùng có phân trang và tìm kiếm.
    /// </summary>
    /// <param name="page">Số trang (bắt đầu từ 1).</param>
    /// <param name="pageSize">Số bản ghi mỗi trang (tối đa 100).</param>
    /// <param name="search">Từ khóa tìm kiếm theo tên, username hoặc email. Null = lấy tất cả.</param>
    /// <returns>PagedResult chứa danh sách UserDto và thông tin phân trang.</returns>
    Task<PagedResult<UserDto>> GetAllUsersAsync(int page, int pageSize, string? search = null);

    /// <summary>
    /// Lấy thông tin chi tiết người dùng theo ID.
    /// </summary>
    /// <param name="id">GUID định danh người dùng.</param>
    /// <returns>UserDto với đầy đủ thông tin bao gồm vai trò và phòng/khoa.</returns>
    /// <exception cref="Exceptions.UserNotFoundException">Khi không tìm thấy người dùng.</exception>
    Task<UserDto> GetUserByIdAsync(Guid id);

    /// <summary>
    /// Lấy thông tin người dùng theo username.
    /// </summary>
    /// <param name="username">Tên đăng nhập của người dùng.</param>
    /// <returns>UserDto nếu tìm thấy.</returns>
    /// <exception cref="Exceptions.UserNotFoundException">Khi không tìm thấy người dùng.</exception>
    Task<UserDto> GetUserByUsernameAsync(string username);

    /// <summary>
    /// Tạo người dùng mới với các vai trò được chỉ định.
    /// </summary>
    /// <param name="dto">DTO chứa thông tin người dùng và danh sách role IDs.</param>
    /// <returns>UserDto của người dùng vừa được tạo.</returns>
    /// <exception cref="Exceptions.UserAlreadyExistsException">Khi username hoặc email đã tồn tại.</exception>
    /// <exception cref="Exceptions.DepartmentNotFoundException">Khi DepartmentId không hợp lệ.</exception>
    Task<UserDto> CreateUserAsync(CreateUserDto dto);

    /// <summary>
    /// Cập nhật thông tin người dùng hiện có.
    /// </summary>
    /// <param name="id">GUID của người dùng cần cập nhật.</param>
    /// <param name="dto">DTO chứa thông tin cần cập nhật.</param>
    /// <returns>UserDto sau khi cập nhật.</returns>
    /// <exception cref="Exceptions.UserNotFoundException">Khi không tìm thấy người dùng.</exception>
    Task<UserDto> UpdateUserAsync(Guid id, UpdateUserDto dto);

    /// <summary>
    /// Xóa người dùng theo ID.
    /// </summary>
    /// <param name="id">GUID của người dùng cần xóa.</param>
    /// <returns>True nếu xóa thành công.</returns>
    /// <exception cref="Exceptions.UserNotFoundException">Khi không tìm thấy người dùng.</exception>
    Task<bool> DeleteUserAsync(Guid id);

    /// <summary>
    /// Gán vai trò cho người dùng.
    /// </summary>
    /// <param name="userId">GUID của người dùng.</param>
    /// <param name="roleId">GUID của vai trò cần gán.</param>
    /// <exception cref="Exceptions.UserNotFoundException">Khi không tìm thấy người dùng.</exception>
    /// <exception cref="Exceptions.RoleNotFoundException">Khi không tìm thấy vai trò.</exception>
    Task AssignRoleAsync(Guid userId, Guid roleId);

    /// <summary>
    /// Xóa vai trò khỏi người dùng.
    /// </summary>
    /// <param name="userId">GUID của người dùng.</param>
    /// <param name="roleId">GUID của vai trò cần xóa.</param>
    Task RemoveRoleAsync(Guid userId, Guid roleId);
}
