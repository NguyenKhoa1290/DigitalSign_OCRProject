using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignService.Core.Common;
using SignService.Core.Interfaces;

namespace SignService.API.Controllers;

[ApiController, Authorize, Route("api/signatures/verify-file")]
public sealed class PdfVerificationController(IPdfSigningService pdf) : ControllerBase
{
    [HttpPost, RequestSizeLimit(54L * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 54L * 1024 * 1024)]
    public async Task<IActionResult> Verify(IFormFile file, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        if (file == null || file.Length == 0 || file.Length > 50L * 1024 * 1024 || !file.FileName.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<object>.Fail("Chọn PDF không quá 50 MB để kiểm tra chữ ký."));
        using var stream = new MemoryStream(); await file.CopyToAsync(stream,token);
        if (!stream.ToArray().AsSpan(0,Math.Min((int)stream.Length,5)).SequenceEqual("%PDF-"u8))
            return BadRequest(ApiResponse<object>.Fail("Tệp không phải PDF hợp lệ."));
        // Read-only verification: never save uploaded files or change a document.
        var result = await pdf.VerifyPdfSignaturesAsync(stream.ToArray());
        return Ok(ApiResponse<SignVerifyResult>.Ok(result));
    }
}
