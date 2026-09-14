using IdentityService.Core.DTOs.Roles;
using IdentityService.Core.Exceptions;
using IdentityService.Core.Interfaces;
using IdentityService.Core.Services;

namespace IdentityService.Infrastructure.Services;

/// <summary>
/// Triển khai IRoleService: cung cấp thông tin vai trò trong hệ thống.
/// </summary>
public class RoleService : IRoleService
{
    private readonly IRoleRepository _roleRepository;

    public RoleService(IRoleRepository roleRepository)
        => _roleRepository = roleRepository;

    public async Task<IEnumerable<RoleDto>> GetAllRolesAsync()
    {
        var roles = await _roleRepository.GetAllAsync();
        return roles.Select(r => new RoleDto
        {
            Id          = r.Id,
            RoleName    = r.RoleName,
            Description = r.Description
        });
    }

    public async Task<RoleDto> GetRoleByIdAsync(Guid id)
    {
        var role = await _roleRepository.GetByIdAsync(id)
            ?? throw new RoleNotFoundException($"Không tìm thấy vai trò với Id = {id}.");
        return new RoleDto { Id = role.Id, RoleName = role.RoleName, Description = role.Description };
    }

    public async Task<RoleDto> GetRoleByNameAsync(string roleName)
    {
        var role = await _roleRepository.GetByNameAsync(roleName)
            ?? throw new RoleNotFoundException($"Không tìm thấy vai trò '{roleName}'.");
        return new RoleDto { Id = role.Id, RoleName = role.RoleName, Description = role.Description };
    }
}
