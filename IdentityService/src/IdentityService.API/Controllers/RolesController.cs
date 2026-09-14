using IdentityService.Core.Common;
using IdentityService.Core.DTOs.Roles;
using IdentityService.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityService.API.Controllers;

/// <summary>
/// Quản lý vai trò trong hệ thống RBAC.
/// </summary>
[ApiController]
[Route("api/roles")]
[Authorize]
[Produces("application/json")]
public class RolesController : ControllerBase
{
    private readonly IRoleService _roleService;

    public RolesController(IRoleService roleService)
    {
        _roleService = roleService;
    }

    /// <summary>
    /// Lấy danh sách tất cả vai trò trong hệ thống.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<RoleDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var roles = await _roleService.GetAllRolesAsync();
        return Ok(ApiResponse<IEnumerable<RoleDto>>.Ok(roles, "Lấy danh sách vai trò thành công"));
    }

    /// <summary>
    /// Lấy thông tin vai trò theo ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var role = await _roleService.GetRoleByIdAsync(id);
        return Ok(ApiResponse<RoleDto>.Ok(role, "Lấy thông tin vai trò thành công"));
    }
}
