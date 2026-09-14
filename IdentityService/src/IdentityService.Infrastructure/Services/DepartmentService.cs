using IdentityService.Core.DTOs.Departments;
using IdentityService.Core.Entities;
using IdentityService.Core.Exceptions;
using IdentityService.Core.Interfaces;
using IdentityService.Core.Services;

namespace IdentityService.Infrastructure.Services;

/// <summary>
/// Triển khai IDepartmentService: quản lý phòng/khoa theo cây phân cấp.
/// </summary>
public class DepartmentService : IDepartmentService
{
    private readonly IDepartmentRepository _departmentRepository;

    public DepartmentService(IDepartmentRepository departmentRepository)
        => _departmentRepository = departmentRepository;

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<IEnumerable<DepartmentDto>> GetAllDepartmentsAsync()
    {
        var depts = await _departmentRepository.GetAllAsync();
        return depts.Select(MapToDto);
    }

    public async Task<IEnumerable<DepartmentDto>> GetDepartmentTreeAsync()
    {
        // Lấy tất cả đơn vị gốc (ParentId == null) và đính kèm đệ quy
        var roots = await _departmentRepository.GetChildrenAsync(null);
        return roots.Select(MapToDtoWithChildren);
    }

    public async Task<DepartmentDto> GetDepartmentByIdAsync(Guid id)
    {
        var dept = await _departmentRepository.GetByIdAsync(id)
            ?? throw new DepartmentNotFoundException($"Không tìm thấy phòng/khoa với Id = {id}.");
        return MapToDto(dept);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    public async Task<DepartmentDto> CreateDepartmentAsync(CreateDepartmentDto dto)
    {
        // Validate parent nếu có
        if (dto.ParentId.HasValue)
        {
            var parent = await _departmentRepository.GetByIdAsync(dto.ParentId.Value);
            if (parent is null)
                throw new DepartmentNotFoundException($"Không tìm thấy phòng/khoa cha với Id = {dto.ParentId.Value}.");
        }

        var department = new Department
        {
            Id          = Guid.NewGuid(),
            DeptName    = dto.DeptName.Trim(),
            DeptCode    = dto.DeptCode.Trim().ToUpper(),
            ParentId    = dto.ParentId,
            Description = dto.Description?.Trim(),
            CreatedAt   = DateTime.UtcNow
        };

        var created = await _departmentRepository.CreateAsync(department);
        return MapToDto(created);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public async Task<DepartmentDto> UpdateDepartmentAsync(Guid id, CreateDepartmentDto dto)
    {
        var dept = await _departmentRepository.GetByIdAsync(id)
            ?? throw new DepartmentNotFoundException($"Không tìm thấy phòng/khoa với Id = {id}.");

        // Kiểm tra circular reference đầy đủ (kể cả vòng lặp gián tiếp A→B→C→A)
        if (dto.ParentId.HasValue)
        {
            if (await HasCircularReferenceAsync(targetId: id, proposedParentId: dto.ParentId.Value))
                throw new IdentityServiceException(
                    "Không thể cập nhật: ParentId tạo vòng lặp phân cấp (circular reference). " +
                    "Phòng/khoa được chọn làm cha là con cháu của chính đơn vị này.");
        }

        dept.DeptName    = dto.DeptName.Trim();
        dept.DeptCode    = dto.DeptCode?.Trim().ToUpper() ?? dept.DeptCode;
        dept.ParentId    = dto.ParentId;
        dept.Description = dto.Description?.Trim();

        var updated = await _departmentRepository.UpdateAsync(dept);
        return MapToDto(updated);
    }

    /// <summary>
    /// Duyệt ngược cây tổ tiên của <paramref name="proposedParentId"/>.
    /// Nếu gặp lại <paramref name="targetId"/> trong chuỗi tổ tiên → circular reference.
    ///
    /// Ví dụ: Đang sửa HAU (id=1), muốn đặt parent = TH (id=2)
    ///   TH.parentId = HAU (id=1) → phát hiện id=1 trong chuỗi → return true → throw
    /// </summary>
    private async Task<bool> HasCircularReferenceAsync(Guid targetId, Guid proposedParentId)
    {
        // Trường hợp đơn giản: tự trỏ chính mình
        if (proposedParentId == targetId) return true;

        // Duyệt ngược chuỗi tổ tiên của proposedParent
        var visited  = new HashSet<Guid>();   // chống vòng lặp vô hạn khi DB đã hỏng
        var currentId = proposedParentId;

        while (true)
        {
            // Phát hiện vòng lặp trong chính chuỗi đang duyệt (DB đã corrupt)
            if (!visited.Add(currentId)) return true;

            var current = await _departmentRepository.GetByIdAsync(currentId);

            // Đã lên đến gốc (root) mà không gặp targetId → an toàn
            if (current is null || current.ParentId is null) return false;

            // Tổ tiên của proposedParent chính là targetId → circular!
            if (current.ParentId.Value == targetId) return true;

            currentId = current.ParentId.Value;
        }
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    public async Task<bool> DeleteDepartmentAsync(Guid id)
    {
        var dept = await _departmentRepository.GetByIdAsync(id)
            ?? throw new DepartmentNotFoundException($"Không tìm thấy phòng/khoa với Id = {id}.");
        return await _departmentRepository.DeleteAsync(dept.Id);
    }

    // ── Children ──────────────────────────────────────────────────────────────

    public async Task<IEnumerable<DepartmentDto>> GetChildDepartmentsAsync(Guid? parentId)
    {
        var children = await _departmentRepository.GetChildrenAsync(parentId);
        return children.Select(MapToDto);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static DepartmentDto MapToDto(Department d) => new()
    {
        Id          = d.Id,
        DeptName    = d.DeptName,
        DeptCode    = d.DeptCode,
        ParentId    = d.ParentId,
        ParentName  = d.Parent?.DeptName,
        Description = d.Description,
        CreatedAt   = d.CreatedAt
    };

    private static DepartmentDto MapToDtoWithChildren(Department d)
    {
        var dto = MapToDto(d);
        if (d.Children is { Count: > 0 })
            dto.Children = d.Children.Select(MapToDtoWithChildren).ToList();
        return dto;
    }
}
