using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignService.Core.Common;
using SignService.Core.DTOs;
using SignService.Core.Interfaces;

namespace SignService.API.Controllers;

[ApiController]
[Route("api/signatures/incoming")]
[Authorize]
public class IncomingSignaturesController(IIncomingWorkflow workflow) : ControllerBase
{
    private WorkflowActor Actor() => new(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub") ?? throw new UnauthorizedAccessException()),
        User.FindAll(ClaimTypes.Role).Select(c => c.Value).Concat(User.FindAll("role").Select(c => c.Value)).Distinct().ToArray());

    [HttpPost("{id:guid}/direction")]
    [Authorize(Roles = "BoardOfDirectors")]
    public async Task<IActionResult> Direct(Guid id, IncomingDirectionRequest request)
        => Ok(ApiResponse<SignResultDto>.Ok(await workflow.DirectAsync(id, Actor(), request), "Đã ký chỉ đạo và chuyển đơn vị xử lý."));

    [HttpPost("{id:guid}/assignment")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Assign(Guid id, IncomingAssignmentRequest request)
        => Ok(ApiResponse<SignResultDto>.Ok(await workflow.AssignAsync(id, Actor(), request), "Đã ký phân công chuyên viên."));
}
