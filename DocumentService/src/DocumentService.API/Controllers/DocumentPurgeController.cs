using System.Security.Claims;
using DocumentService.Core.Common;
using DocumentService.Core.DTOs;
using DocumentService.Core.Entities;
using DocumentService.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentService.API.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize(Roles = "Admin")]
public class DocumentPurgeController(IDocumentPurgeService purge) : ControllerBase
{
    [HttpPost("{id:guid}/purge")]
    public async Task<IActionResult> Purge(Guid id, DocumentPurgeDto request)
    {
        var actor = new DocumentActor(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? ""),
            User.FindAll(ClaimTypes.Role).Select(c => c.Value).Concat(User.FindAll("role").Select(c => c.Value)).ToArray());
        var result = await purge.PurgeAsync(id, actor, request);
        return Ok(ApiResponse<DocumentPurgeResult>.Ok(result, result.FilesPendingCleanup
            ? "Đã xóa hồ sơ. Tệp đang chờ dọn tự động." : "Đã xóa vĩnh viễn công văn."));
    }
}
