using IdentityService.Core.Common;
using IdentityService.Core.DTOs.Users;
using IdentityService.Core.Entities;
using IdentityService.Core.Exceptions;
using IdentityService.Core.Interfaces;
using IdentityService.Core.Services;

namespace IdentityService.Infrastructure.Services;

/// <summary>
/// Triển khai IUserService: quản lý người dùng với DTO mapping và BCrypt password hashing.
/// </summary>
public class UserService : IUserService
{
    private readonly IUserRepository       _userRepository;
    private readonly IRoleRepository       _roleRepository;
    private readonly IDepartmentRepository _departmentRepository;

    public UserService(
        IUserRepository       userRepository,
        IRoleRepository       roleRepository,
        IDepartmentRepository departmentRepository)
    {
        _userRepository       = userRepository;
        _roleRepository       = roleRepository;
        _departmentRepository = departmentRepository;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<PagedResult<UserDto>> GetAllUsersAsync(int page, int pageSize, string? search = null)
    {
        var (users, total) = await _userRepository.GetAllAsync(page, pageSize, search);
        return PagedResult<UserDto>.Create(users.Select(MapToDto).ToList(), total, page, pageSize);
    }

    public async Task<UserDto> GetUserByIdAsync(Guid id)
    {
        var user = await _userRepository.GetByIdAsync(id)
            ?? throw new UserNotFoundException($"Không tìm thấy người dùng với Id = {id}.");
        return MapToDto(user);
    }

    public async Task<UserDto> GetUserByUsernameAsync(string username)
    {
        var user = await _userRepository.GetByUsernameAsync(username)
            ?? throw new UserNotFoundException($"Không tìm thấy người dùng '{username}'.");
        return MapToDto(user);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    public async Task<UserDto> CreateUserAsync(CreateUserDto dto)
    {
        // Validate uniqueness
        var existingByUsername = await _userRepository.GetByUsernameAsync(dto.Username);
        if (existingByUsername is not null)
            throw new UserAlreadyExistsException("username", dto.Username);

        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            var existingByEmail = await _userRepository.GetByEmailAsync(dto.Email);
            if (existingByEmail is not null)
                throw new UserAlreadyExistsException("email", dto.Email);
        }

        // Validate department
        if (dto.DepartmentId.HasValue)
        {
            var dept = await _departmentRepository.GetByIdAsync(dto.DepartmentId.Value);
            if (dept is null)
                throw new DepartmentNotFoundException($"Không tìm thấy phòng/khoa với Id = {dto.DepartmentId.Value}.");
        }

        // Validate roles
        foreach (var roleId in dto.RoleIds ?? new List<Guid>())
        {
            var role = await _roleRepository.GetByIdAsync(roleId);
            if (role is null)
                throw new RoleNotFoundException($"Không tìm thấy vai trò với Id = {roleId}.");
        }

        // Hash password
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, workFactor: 12);

        var user = new AppUser
        {
            Id                 = Guid.NewGuid(),
            Username           = dto.Username.Trim(),
            PasswordHash       = passwordHash,
            FullName           = dto.FullName.Trim(),
            Email              = dto.Email?.Trim(),
            PhoneNumber        = dto.PhoneNumber?.Trim(),
            DepartmentId       = dto.DepartmentId,
            IsActive           = true,
            MustChangePassword = true,   // Bắt buộc đổi mật khẩu lần đầu đăng nhập
            CreatedAt          = DateTime.UtcNow,
            UserRoles          = (dto.RoleIds ?? new List<Guid>())
                .Select(rid => new AppUserRole { RoleId = rid })
                .ToList()
        };

        var created = await _userRepository.CreateAsync(user);
        return MapToDto(created);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public async Task<UserDto> UpdateUserAsync(Guid id, UpdateUserDto dto)
    {
        var user = await _userRepository.GetByIdAsync(id)
            ?? throw new UserNotFoundException($"Không tìm thấy người dùng với Id = {id}.");

        // Email uniqueness check (only if email is changing)
        if (!string.IsNullOrWhiteSpace(dto.Email) &&
            !string.Equals(user.Email, dto.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await _userRepository.GetByEmailAsync(dto.Email);
            if (existing is not null)
                throw new UserAlreadyExistsException("email", dto.Email);
        }

        // Department validation
        if (dto.DepartmentId.HasValue)
        {
            var dept = await _departmentRepository.GetByIdAsync(dto.DepartmentId.Value);
            if (dept is null)
                throw new DepartmentNotFoundException($"Không tìm thấy phòng/khoa với Id = {dto.DepartmentId.Value}.");
        }

        // Apply changes
        if (!string.IsNullOrWhiteSpace(dto.FullName))     user.FullName    = dto.FullName.Trim();
        if (dto.Email is not null)                        user.Email       = dto.Email.Trim();
        if (dto.PhoneNumber is not null)                  user.PhoneNumber = dto.PhoneNumber.Trim();
        if (dto.DepartmentId.HasValue)                    user.DepartmentId = dto.DepartmentId;
        user.IsActive = dto.IsActive;

        var updated = await _userRepository.UpdateAsync(user);
        return MapToDto(updated);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    public async Task<bool> DeleteUserAsync(Guid id)
    {
        var user = await _userRepository.GetByIdAsync(id)
            ?? throw new UserNotFoundException($"Không tìm thấy người dùng với Id = {id}.");
        return await _userRepository.DeleteAsync(user.Id);
    }

    // ── Role Management ───────────────────────────────────────────────────────

    public async Task AssignRoleAsync(Guid userId, Guid roleId)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new UserNotFoundException($"Không tìm thấy người dùng với Id = {userId}.");
        var role = await _roleRepository.GetByIdAsync(roleId)
            ?? throw new RoleNotFoundException($"Không tìm thấy vai trò với Id = {roleId}.");
        await _userRepository.AssignRoleAsync(userId, roleId);
    }

    public async Task RemoveRoleAsync(Guid userId, Guid roleId)
    {
        await _userRepository.RemoveRoleAsync(userId, roleId);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static UserDto MapToDto(AppUser user) => new()
    {
        Id           = user.Id,
        Username     = user.Username,
        FullName     = user.FullName,
        Email        = user.Email,
        PhoneNumber  = user.PhoneNumber,
        DepartmentId = user.DepartmentId,
        DepartmentName = user.Department?.DeptName,
        IsActive     = user.IsActive,
        CreatedAt    = user.CreatedAt,
        Roles        = user.UserRoles?.Select(ur => ur.Role?.RoleName ?? "").Where(r => r != "").ToList() ?? new List<string>()
    };
}
