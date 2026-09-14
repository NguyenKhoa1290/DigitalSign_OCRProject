using IdentityService.Core.Common;
using IdentityService.Core.DTOs.Users;
using IdentityService.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityService.API.Controllers;

/// <summary>
/// Quản lý người dùng (CRUD). Chỉ Admin có quyền thêm/sửa/xóa.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserService userService, ILogger<UsersController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Lấy danh sách người dùng có phân trang và tìm kiếm.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null)
    {
        var result = await _userService.GetAllUsersAsync(page, pageSize, search);
        return Ok(ApiResponse<PagedResult<UserDto>>.Ok(result, "Lấy danh sách người dùng thành công"));
    }

    /// <summary>
    /// Lấy thông tin người dùng theo ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        return Ok(ApiResponse<UserDto>.Ok(user, "Lấy thông tin người dùng thành công"));
    }

    /// <summary>
    /// Tạo tài khoản người dùng mới (chỉ Admin).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateUserDto request)
    {
        _logger.LogInformation("Admin creating new user: {Username}", request.Username);
        var user = await _userService.CreateUserAsync(request);
        _logger.LogInformation("User created successfully: {UserId}", user.Id);
        return CreatedAtAction(nameof(GetById), new { id = user.Id },
            ApiResponse<UserDto>.Ok(user, "Tạo tài khoản thành công"));
    }

    /// <summary>
    /// Cập nhật thông tin người dùng (Admin hoặc chính người dùng đó).
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDto request)
    {
        var currentUserId = User.FindFirst("sub")?.Value ?? User.FindFirst("nameid")?.Value;
        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && currentUserId != id.ToString())
            return Forbid();

        var user = await _userService.UpdateUserAsync(id, request);
        return Ok(ApiResponse<UserDto>.Ok(user, "Cập nhật thông tin thành công"));
    }

    /// <summary>
    /// Xóa tài khoản người dùng (chỉ Admin).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _userService.DeleteUserAsync(id);
        return Ok(ApiResponse<bool>.Ok(result, "Xóa tài khoản thành công"));
    }

    /// <summary>
    /// Gán vai trò cho người dùng (chỉ Admin).
    /// </summary>
    [HttpPost("{id:guid}/roles/{roleId:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignRole(Guid id, Guid roleId)
    {
        await _userService.AssignRoleAsync(id, roleId);
        return Ok(ApiResponse<bool>.Ok(true, "Gán vai trò thành công"));
    }

    /// <summary>
    /// Thu hồi vai trò của người dùng (chỉ Admin).
    /// </summary>
    [HttpDelete("{id:guid}/roles/{roleId:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveRole(Guid id, Guid roleId)
    {
        await _userService.RemoveRoleAsync(id, roleId);
        return Ok(ApiResponse<bool>.Ok(true, "Thu hồi vai trò thành công"));
    }

    /// <summary>
    /// Lấy thông tin người dùng hiện tại (từ JWT).
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst("nameid")?.Value;
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var userGuid))
            return Unauthorized();

        var user = await _userService.GetUserByIdAsync(userGuid);
        return Ok(ApiResponse<UserDto>.Ok(user, "Lấy thông tin người dùng thành công"));
    }
}
