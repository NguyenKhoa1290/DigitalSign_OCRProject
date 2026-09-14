using IdentityService.Core.DTOs.Departments;

namespace IdentityService.Core.Services;

/// <summary>
/// Giao diện dịch vụ quản lý phòng/khoa (Department Management Service).
/// Hỗ trợ cả thao tác CRUD và truy vấn cấu trúc phân cấp.
/// </summary>
public interface IDepartmentService
{
    /// <summary>
    /// Lấy danh sách tất cả phòng/khoa (danh sách phẳng, kèm thông tin cha).
    /// </summary>
    /// <returns>Danh sách DepartmentDto.</returns>
    Task<IEnumerable<DepartmentDto>> GetAllDepartmentsAsync();

    /// <summary>
    /// Lấy cấu trúc cây phân cấp đầy đủ của tổ chức.
    /// Chỉ trả về các đơn vị gốc (ParentId = null) với Children được đính kèm đệ quy.
    /// </summary>
    /// <returns>Danh sách DepartmentDto gốc có Children lồng nhau.</returns>
    Task<IEnumerable<DepartmentDto>> GetDepartmentTreeAsync();

    /// <summary>
    /// Lấy thông tin chi tiết phòng/khoa theo ID.
    /// </summary>
    /// <param name="id">GUID định danh phòng/khoa.</param>
    /// <returns>DepartmentDto nếu tìm thấy.</returns>
    /// <exception cref="Exceptions.DepartmentNotFoundException">Khi không tìm thấy phòng/khoa.</exception>
    Task<DepartmentDto> GetDepartmentByIdAsync(Guid id);

    /// <summary>
    /// Tạo phòng/khoa mới.
    /// </summary>
    /// <param name="dto">DTO chứa thông tin phòng/khoa cần tạo.</param>
    /// <returns>DepartmentDto của phòng/khoa vừa được tạo.</returns>
    /// <exception cref="Exceptions.DepartmentNotFoundException">Khi ParentId được cung cấp nhưng không tồn tại.</exception>
    Task<DepartmentDto> CreateDepartmentAsync(CreateDepartmentDto dto);

    /// <summary>
    /// Cập nhật thông tin phòng/khoa hiện có.
    /// </summary>
    /// <param name="id">GUID của phòng/khoa cần cập nhật.</param>
    /// <param name="dto">DTO chứa thông tin cập nhật.</param>
    /// <returns>DepartmentDto sau khi cập nhật.</returns>
    /// <exception cref="Exceptions.DepartmentNotFoundException">Khi không tìm thấy phòng/khoa.</exception>
    Task<DepartmentDto> UpdateDepartmentAsync(Guid id, CreateDepartmentDto dto);

    /// <summary>
    /// Xóa phòng/khoa theo ID.
    /// Lưu ý: Không được xóa nếu phòng/khoa còn phòng/khoa con hoặc có người dùng thuộc về.
    /// </summary>
    /// <param name="id">GUID của phòng/khoa cần xóa.</param>
    /// <returns>True nếu xóa thành công.</returns>
    /// <exception cref="Exceptions.DepartmentNotFoundException">Khi không tìm thấy phòng/khoa.</exception>
    Task<bool> DeleteDepartmentAsync(Guid id);

    /// <summary>
    /// Lấy danh sách phòng/khoa con trực tiếp của một phòng/khoa cha.
    /// </summary>
    /// <param name="parentId">GUID của phòng/khoa cha. Null = lấy tất cả đơn vị gốc.</param>
    /// <returns>Danh sách DepartmentDto con.</returns>
    Task<IEnumerable<DepartmentDto>> GetChildDepartmentsAsync(Guid? parentId);
}
