using IdentityService.Core.Entities;

namespace IdentityService.Core.Interfaces;

/// <summary>
/// Giao diện repository cho thao tác dữ liệu phòng/khoa (Department).
/// Được triển khai tại tầng Infrastructure.
/// </summary>
public interface IDepartmentRepository
{
    /// <summary>Lấy toàn bộ danh sách phòng/khoa (bao gồm thông tin cấp cha).</summary>
    /// <returns>Danh sách tất cả Department trong hệ thống.</returns>
    Task<IEnumerable<Department>> GetAllAsync();

    /// <summary>Tìm phòng/khoa theo ID.</summary>
    /// <param name="id">GUID định danh phòng/khoa.</param>
    /// <returns>Department nếu tồn tại, null nếu không tìm thấy.</returns>
    Task<Department?> GetByIdAsync(Guid id);

    /// <summary>Tạo phòng/khoa mới.</summary>
    /// <param name="department">Đối tượng Department cần tạo.</param>
    /// <returns>Department vừa được tạo (kèm Id được gán).</returns>
    Task<Department> CreateAsync(Department department);

    /// <summary>Cập nhật thông tin phòng/khoa.</summary>
    /// <param name="department">Đối tượng Department với thông tin đã cập nhật.</param>
    /// <returns>Department sau khi cập nhật.</returns>
    Task<Department> UpdateAsync(Department department);

    /// <summary>Xóa phòng/khoa theo ID.</summary>
    /// <param name="id">GUID định danh phòng/khoa cần xóa.</param>
    /// <returns>True nếu xóa thành công, False nếu không tìm thấy.</returns>
    Task<bool> DeleteAsync(Guid id);

    /// <summary>Lấy danh sách phòng/khoa con trực tiếp của một phòng/khoa cha.</summary>
    /// <param name="parentId">GUID của phòng/khoa cha. Null để lấy các đơn vị gốc.</param>
    /// <returns>Danh sách Department con trực thuộc.</returns>
    Task<IEnumerable<Department>> GetChildrenAsync(Guid? parentId);
}
