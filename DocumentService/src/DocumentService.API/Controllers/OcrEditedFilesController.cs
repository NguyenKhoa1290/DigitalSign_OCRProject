using System.Security.Claims;
using DocumentService.API.Services;
using DocumentService.Core.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentService.API.Controllers;

[ApiController,Authorize(Roles="Admin,Clerk"),Route("api/documents/{documentId:guid}/ocr-edited-file")]
public sealed class OcrEditedFilesController(OcrEditedFileStore edits):ControllerBase
{
    [HttpPost,RequestSizeLimit(64L*1024*1024),RequestFormLimits(MultipartBodyLengthLimit=64L*1024*1024)]
    public async Task<IActionResult> Upload(Guid documentId,[FromQuery]Guid? expectedVersion,IFormFile file,CancellationToken token)
    {
        if(file==null || file.Length==0)return BadRequest(ApiResponse<object>.Fail("Chọn bản Word .docx đã chỉnh sửa."));
        if(!expectedVersion.HasValue)return BadRequest(ApiResponse<object>.Fail("Thiếu phiên bản công văn. Hãy tải lại trang."));
        if(file.Length>50L*1024*1024)return StatusCode(413,ApiResponse<object>.Fail("Tệp vượt quá giới hạn 50 MB."));
        var actor=Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)??User.FindFirstValue("sub")!);
        await using var content=new MemoryStream();await file.CopyToAsync(content,token);content.Position=0;
        return Ok(ApiResponse<OcrEditedFile>.Ok(await edits.UploadAsync(documentId,actor,expectedVersion.Value,content,file.FileName,token),"Đã lưu bản Word OCR đã chỉnh sửa."));
    }
    [HttpGet]
    public async Task<IActionResult> Download(Guid documentId,[FromQuery]Guid revisionId,CancellationToken token)
    {
        Response.Headers.CacheControl="no-store";
        var file=await edits.DownloadAsync(documentId,revisionId,token);return File(file.Content,OcrEditedFileStore.WordMime,file.FileName);
    }
}
