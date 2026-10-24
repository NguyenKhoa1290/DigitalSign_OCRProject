using HauDocumentApp.Models;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HauDocumentApp.Services;

public class AdminService
{
    private readonly ApiService _api;
    public AdminService(ApiService api) => _api = api;

    // ── Users ── (IdentityService: GET/POST/PUT/DELETE api/users)
    public async Task<PagedResult<UserDto>?> GetUsersAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        Guid? departmentId = null)
    {
        var q = $"api/users?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(search)) q += $"&search={Uri.EscapeDataString(search)}";
        if (departmentId.HasValue) q += $"&departmentId={departmentId.Value}";
        return await _api.GetAsync<PagedResult<UserDto>>(q);
    }

    public async Task<UserDto?> GetUserAsync(Guid id) => await _api.GetAsync<UserDto>($"api/users/{id}");

    public async Task<UserDto?> GetCurrentUserAsync() => await _api.GetAsync<UserDto>("api/users/me");

    public async Task<UserDto?> CreateUserAsync(CreateUserDto dto)
    {
        // Lấy danh sách roles để chuyển tên role → GUID
        var roles = await GetRolesAsync();
        var roleId = roles?.FirstOrDefault(r =>
            string.Equals(r.RoleName, dto.Role, StringComparison.OrdinalIgnoreCase))?.Id;
        if (!roleId.HasValue)
            throw new HttpRequestException("Không tải được vai trò đã chọn. Vui lòng thử lại.", null,
                HttpStatusCode.ServiceUnavailable);

        // Gửi request đúng định dạng backend (RoleIds thay vì Role string)
        var backendRequest = new
        {
            Username     = dto.Username,
            FullName     = dto.FullName,
            Email        = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim(),
            Password     = dto.Password,
            PhoneNumber  = dto.PhoneNumber,
            DepartmentId = dto.DepartmentId,
            RoleIds      = new List<Guid> { roleId.Value }
        };

        using var response = await _api.PostResponseAsync("api/users", backendRequest);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(await ReadCreateUserErrorAsync(response), null, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ApiResponse<UserDto>>())?.Data;
    }

    private static async Task<string> ReadCreateUserErrorAsync(HttpResponseMessage response)
    {
        const string fallback = "Không thể tạo người dùng. Vui lòng thử lại sau.";
        if ((int)response.StatusCode >= 500) return fallback;
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            var message = root.TryGetProperty("message", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                if (message?.Contains("username", StringComparison.OrdinalIgnoreCase) == true)
                    return "Mã cán bộ/giảng viên này đã tồn tại. Vui lòng chọn mã khác.";
                if (message?.Contains("email", StringComparison.OrdinalIgnoreCase) == true)
                    return "Email này đã được sử dụng. Vui lòng dùng email khác hoặc để trống.";
                return "Mã cán bộ/giảng viên hoặc email đã tồn tại. Vui lòng kiểm tra lại.";
            }
            if (response.StatusCode == HttpStatusCode.BadRequest && root.TryGetProperty("errors", out var errors))
            {
                var entries = errors.ValueKind == JsonValueKind.Object
                    ? errors.EnumerateObject().SelectMany(p => p.Value.ValueKind == JsonValueKind.Array
                        ? p.Value.EnumerateArray().ToArray() : new[] { p.Value })
                    : errors.ValueKind == JsonValueKind.Array ? errors.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
                var messages = entries.Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
                if (messages.Length > 0) return string.Join(" ", messages);
            }
            return string.IsNullOrWhiteSpace(message) ? fallback : message;
        }
        catch (JsonException) { return fallback; }
    }
    public async Task<UserDto?> UpdateUserAsync(Guid id, UpdateUserDto dto) => await _api.PutAsync<UserDto>($"api/users/{id}", dto);
    public async Task<bool> DeleteUserAsync(Guid id) { try { await _api.DeleteAsync($"api/users/{id}"); return true; } catch { return false; } }
    public async Task<bool> ResetUserPasswordAsync(Guid id, string temporaryPassword, string confirmPassword)
        => await _api.PostAsync<bool>($"api/users/{id}/reset-password", new { TemporaryPassword = temporaryPassword, ConfirmPassword = confirmPassword }) == true;

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
    public async Task<CertificateDto?> GetCertificateAsync(Guid userId) => await _api.GetAsync<CertificateDto>($"api/signatures/certificates/{userId}");
    public async Task<List<CertificateDto>?> GetCertificatesAsync() => await _api.GetAsync<List<CertificateDto>>("api/signatures/certificates");
    // POST api/signatures/certificates/issue
    public async Task<CertificateDto?> IssueCertificateAsync(IssueCertificateDto dto) => await _api.PostAsync<CertificateDto>("api/signatures/certificates/issue", dto);
    public async Task<bool> RevokeCertificateAsync(Guid userId)
        => await _api.DeleteWithResultAsync($"api/signatures/certificates/{userId}");

    // Independent sources: one unavailable service must not erase valid counts from another.
    public async Task<DashboardStats> GetStatsAsync(bool includeAdministration = false)
    {
        var documents = ReadStatisticsAsync("api/documents/stats");
        var users = includeAdministration ? ReadStatisticsAsync("api/users/stats") : Task.FromResult<DashboardStats?>(null);
        var certificates = includeAdministration ? ReadStatisticsAsync("api/signatures/certificates/stats") : Task.FromResult<DashboardStats?>(null);
        await Task.WhenAll(documents, users, certificates);
        var stats = await documents ?? new DashboardStats();
        stats.TotalUsers = (await users)?.TotalUsers;
        stats.ActiveCertificates = (await certificates)?.ActiveCertificates;
        stats.HasUnavailableData = stats.TotalDocuments == null
            || (includeAdministration && (stats.TotalUsers == null || stats.ActiveCertificates == null));
        return stats;
    }

    private async Task<DashboardStats?> ReadStatisticsAsync(string route)
    {
        try { return await _api.GetAsync<DashboardStats>(route); }
        catch (HttpRequestException) { return null; }
        catch (OperationCanceledException) { return null; }
    }

    // ── Roles ── (IdentityService: GET api/roles)
    public async Task<List<RoleDto>?> GetRolesAsync() => await _api.GetAsync<List<RoleDto>>("api/roles");

    public Task<GeminiSettingsStatus?> GetGeminiSettingsAsync()
        => _api.GetAsync<GeminiSettingsStatus>("api/admin/gemini-settings");

    public Task<GeminiSettingsSaveResult?> SaveGeminiApiKeyAsync(string apiKey)
        => _api.PutAsync<GeminiSettingsSaveResult>("api/admin/gemini-settings", new { apiKey });

    public Task<bool> RemoveGeminiApiKeyAsync()
        => _api.DeleteWithResultAsync("api/admin/gemini-settings");
}

public class GeminiSettingsStatus
{
    public bool Configured { get; set; }
    public string? MaskedKey { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class GeminiSettingsSaveResult : GeminiSettingsStatus
{
    public List<string> Models { get; set; } = new();
}

public class DashboardStats
{
    public int? TotalUsers { get; set; }
    public int? TotalDocuments { get; set; }
    public int? TodayDocuments { get; set; }
    public int? PendingDocuments { get; set; }
    public int? PublishedDocuments { get; set; }
    public int? ActiveCertificates { get; set; }
    public int? PendingOcrDocuments { get; set; }
    public int? MyDraftDocuments { get; set; }
    public int? MyPendingDocuments { get; set; }
    public int? PendingDeptDocuments { get; set; }
    public int? PendingDirectorDocuments { get; set; }
    public int? DirectorSignedDocuments { get; set; }
    public int? AssignedDocuments { get; set; }
    public bool HasUnavailableData { get; set; }
}
