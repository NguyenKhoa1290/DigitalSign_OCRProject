using System.Security.Claims;
using DocumentService.API.Services;
using DocumentService.Core.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentService.API.Controllers;

[ApiController, Authorize, Route("api/documents/{documentId:guid}/ocr-jobs")]
public class OcrJobsController(OcrJobStore jobs) : ControllerBase
{
    [HttpPost, Authorize(Roles="Admin,Clerk")]
    public async Task<IActionResult> Create(Guid documentId,[FromBody] OcrJobRequest request,CancellationToken token)
    {
        var actor=Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        var job=await jobs.EnqueueAsync(documentId,actor,request,token);
        return Accepted($"/api/documents/{documentId}/ocr-jobs/latest",ApiResponse<OcrJobDto>.Ok(job,"Đã tiếp nhận OCR. Bạn có thể tiếp tục thao tác; hệ thống sẽ thông báo khi hoàn tất."));
    }
    [HttpGet("latest")]
    public async Task<IActionResult> Latest(Guid documentId,CancellationToken token)
    {
        Response.Headers.CacheControl="no-store";
        return Ok(ApiResponse<OcrJobDto?>.Ok(await jobs.LatestAsync(documentId,token)));
    }
}
