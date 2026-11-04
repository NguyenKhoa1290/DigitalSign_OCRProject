using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using DocumentService.API.Services;
using DocumentService.Core.Common;
using DocumentService.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentService.API.Controllers;

[ApiController, Authorize, Route("api/documents/{documentId:guid}/ocr-word")]
public sealed class OcrWordController(IDocumentService documents, OcrEditedFileStore edits, IHttpClientFactory clients) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Download(Guid documentId,[FromQuery]Guid? expectedVersion,CancellationToken token)
    {
        Response.Headers.CacheControl="no-store";
        var document=await documents.GetDocumentByIdAsync(documentId);
        var actor=User.FindFirstValue(ClaimTypes.NameIdentifier)??User.FindFirstValue("sub");
        var canDownload=User.IsInRole("Admin")||User.IsInRole("Clerk")
            ||User.IsInRole("Specialist")&&document.DocTypeName!="CongVanDen"&&Guid.TryParse(actor,out var user)&&document.CreatedById==user;
        if(!canDownload)return Forbid();
        if(!expectedVersion.HasValue)return BadRequest(ApiResponse<object>.Fail("Thiếu phiên bản công văn."));
        if(document.ConcurrencyVersion!=expectedVersion.Value)return Conflict(ApiResponse<object>.Fail("Kết quả đã thay đổi. Hãy tải lại công văn trước khi tải Word."));
        if(document.MinioPath.EndsWith(".docx",StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<object>.Fail("Word là văn bản hoàn chỉnh. Hãy tải tệp đính kèm trực tiếp."));
        JsonObject? raw;
        try{raw=JsonNode.Parse(document.OcrDataRaw??"null") as JsonObject;}
        catch(JsonException){return BadRequest(ApiResponse<object>.Fail("Kết quả OCR không hợp lệ."));}
        if(raw?["provider"]?.ToString()!="gemini"||raw["layout"]?["blocks"] is not JsonArray{Count:>0})
            return BadRequest(ApiResponse<object>.Fail("Chưa có kết quả OCR để tải Word."));
        if(raw["sourcePath"] is {} path&&path.ToString()!=document.MinioPath)
            return Conflict(ApiResponse<object>.Fail("Tệp nguồn đã thay đổi. Hãy kiểm tra lại kết quả OCR."));
        if(Guid.TryParse(raw["editedDocument"]?["id"]?.ToString(),out var revision))
        {
            var file=await edits.DownloadAsync(documentId,revision,token);
            return File(file.Content,OcrEditedFileStore.WordMime,file.FileName);
        }
        using var client=clients.CreateClient("ocr-background");
        using var response=await client.PostAsJsonAsync("api/ocr/internal/export-layout",new{layout=raw["layout"]},token);
        if(!response.IsSuccessStatusCode)return StatusCode(502,ApiResponse<object>.Fail("Chưa xuất được Word. Hãy thử lại; không cần chạy lại OCR."));
        return File(await response.Content.ReadAsByteArrayAsync(token),OcrEditedFileStore.WordMime,"ket-qua-ocr.docx");
    }
}
