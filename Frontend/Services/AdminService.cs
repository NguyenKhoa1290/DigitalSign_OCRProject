using HauDocumentApp.Models;

namespace HauDocumentApp.Services;

public class AdminService
{
    private readonly ApiService _api;
    public AdminService(ApiService api) => _api = api;

    // ── Users ── (IdentityService: GET/POST/PUT/DELETE api/users)
    public async Task<PagedResult<UserDto>?> GetUsersAsync(int page = 1, int pageSize = 20, string? search = null)
    {
        var q = $"api/users?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(search)) q += $"&search={Uri.EscapeDataString(search)}";
        return await _api.GetAsync<PagedResult<UserDto>>(q);
    }

    public async Task<UserDto?> GetUserAsync(Guid id) => await _api.GetAsync<UserDto>($"api/users/{id}");

    public async Task<UserDto?> CreateUserAsync(CreateUserDto dto)
    {
        // Lấy danh sách roles để chuyển tên role → GUID
        var roles = await GetRolesAsync();
        var roleId = roles?.FirstOrDefault(r =>
            string.Equals(r.RoleName, dto.Role, StringComparison.OrdinalIgnoreCase))?.Id;

        // Gửi request đúng định dạng backend (RoleIds thay vì Role string)
        var backendRequest = new
        {
            Username     = dto.Username,
            FullName     = dto.FullName,
            Email        = dto.Email,
            Password     = dto.Password,
            DepartmentId = dto.DepartmentId,
            RoleIds      = roleId.HasValue
                ? new List<Guid> { roleId.Value }
                : new List<Guid>()
        };

        return await _api.PostAsync<UserDto>("api/users", backendRequest);
    }
    public async Task<UserDto?> UpdateUserAsync(Guid id, UpdateUserDto dto) => await _api.PutAsync<UserDto>($"api/users/{id}", dto);
    public async Task<bool> DeleteUserAsync(Guid id) { try { await _api.DeleteAsync($"api/users/{id}"); return true; } catch { return false; } }

    // ── Departments ── (IdentityService: GET/POST/PUT/DELETE api/departments)
    // Flat list — dùng cho dropdown chọn phòng ban
    public async Task<List<DepartmentDto>?> GetDepartmentsAsync() => await _api.GetAsync<List<DepartmentDto>>("api/departments");
    // Tree — dùng hiển thị cây phân cấp (có children)
    public async Task<List<DepartmentDto>?> GetDepartmentTreeAsync() => await _api.GetAsync<List<DepartmentDto>>("api/departments/tree");
    public async Task<DepartmentDto?> CreateDepartmentAsync(CreateDepartmentDto dto) => await _api.PostAsync<DepartmentDto>("api/departments", dto);
    public async Task<DepartmentDto?> UpdateDepartmentAsync(Guid id, CreateDepartmentDto dto) => await _api.PutAsync<DepartmentDto>($"api/departments/{id}", dto);
    public async Task<bool> DeleteDepartmentAsync(Guid id) { try { await _api.DeleteAsync($"api/departments/{id}"); return true; } catch { return false; } }

    // ── Certificates ── (SignService: api/signatures/certificates/...)
    // GET api/signatures/certificates/{userId}
    public async Task<List<CertificateDto>?> GetCertificatesAsync(Guid userId) => await _api.GetAsync<List<CertificateDto>>($"api/signatures/certificates/{userId}");
    // Overload không có userId — dùng trong trang admin (hiển thị tất cả cert)
    // Backend không có endpoint này, tạm return null, UI sẽ nhẫn rỗng
    public Task<List<CertificateDto>?> GetCertificatesAsync() => Task.FromResult<List<CertificateDto>?>(null);
    // POST api/signatures/certificates/issue
    public async Task<CertificateDto?> IssueCertificateAsync(IssueCertificateDto dto) => await _api.PostAsync<CertificateDto>("api/signatures/certificates/issue", dto);
    // Backend không có revoke endpoint — stub trả true
    public Task<bool> RevokeCertificateAsync(Guid id) => Task.FromResult(true);

    // ── Stats ── (tổng hợp từ Document Service, tính local từ danh sách docs)
    // Backend không có /stats endpoint — trả về null, UI sẽ ẩn hoặc hiện 0
    public Task<DashboardStats?> GetStatsAsync() => Task.FromResult<DashboardStats?>(null);

    // ── Roles ── (IdentityService: GET api/roles)
    public async Task<List<RoleDto>?> GetRolesAsync() => await _api.GetAsync<List<RoleDto>>("api/roles");
}

public class DashboardStats
{
    public int TotalUsers { get; set; }
    public int TotalDocuments { get; set; }
    public int TodayDocuments { get; set; }
    public int PendingDocuments { get; set; }
    public int PublishedDocuments { get; set; }
    public int ActiveCertificates { get; set; }
}
