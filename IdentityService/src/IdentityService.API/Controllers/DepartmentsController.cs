using IdentityService.Core.Common;
using IdentityService.Core.DTOs.Departments;
using IdentityService.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityService.API.Controllers;

/// <summary>
/// Quản lý sơ đồ tổ chức Phòng/Ban/Khoa của Trường.
/// </summary>
[ApiController]
[Route("api/departments")]
[Authorize]
[Produces("application/json")]
public class DepartmentsController : ControllerBase
{
    private readonly IDepartmentService _departmentService;
    private readonly ILogger<DepartmentsController> _logger;

    public DepartmentsController(IDepartmentService departmentService, ILogger<DepartmentsController> logger)
    {
        _departmentService = departmentService;
        _logger = logger;
    }

    /// <summary>
    /// Lấy danh sách tất cả đơn vị (danh sách phẳng).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<DepartmentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var departments = await _departmentService.GetAllDepartmentsAsync();
        return Ok(ApiResponse<IEnumerable<DepartmentDto>>.Ok(departments, "Lấy danh sách đơn vị thành công"));
    }

    /// <summary>
    /// Lấy cấu trúc cây phân cấp của tổ chức.
    /// </summary>
    [HttpGet("tree")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<DepartmentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTree()
    {
        var tree = await _departmentService.GetDepartmentTreeAsync();
        return Ok(ApiResponse<IEnumerable<DepartmentDto>>.Ok(tree, "Lấy cấu trúc cây đơn vị thành công"));
    }

    /// <summary>
    /// Lấy thông tin đơn vị theo ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var department = await _departmentService.GetDepartmentByIdAsync(id);
        return Ok(ApiResponse<DepartmentDto>.Ok(department, "Lấy thông tin đơn vị thành công"));
    }

    /// <summary>
    /// Tạo mới đơn vị tổ chức (chỉ Admin).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentDto request)
    {
        _logger.LogInformation("Creating department: {DeptName}", request.DeptName);
        var department = await _departmentService.CreateDepartmentAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = department.Id },
            ApiResponse<DepartmentDto>.Ok(department, "Tạo đơn vị thành công"));
    }

    /// <summary>
    /// Cập nhật thông tin đơn vị (chỉ Admin).
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateDepartmentDto request)
    {
        var department = await _departmentService.UpdateDepartmentAsync(id, request);
        return Ok(ApiResponse<DepartmentDto>.Ok(department, "Cập nhật đơn vị thành công"));
    }

    /// <summary>
    /// Xóa đơn vị (chỉ Admin, không có đơn vị con hoặc người dùng).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _departmentService.DeleteDepartmentAsync(id);
        return Ok(ApiResponse<bool>.Ok(result, "Xóa đơn vị thành công"));
    }

    /// <summary>
    /// Lấy danh sách đơn vị con trực tiếp của một đơn vị cha.
    /// </summary>
    [HttpGet("{id:guid}/children")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<DepartmentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChildren(Guid id)
    {
        var children = await _departmentService.GetChildDepartmentsAsync(id);
        return Ok(ApiResponse<IEnumerable<DepartmentDto>>.Ok(children, "Lấy danh sách đơn vị con thành công"));
    }
}
