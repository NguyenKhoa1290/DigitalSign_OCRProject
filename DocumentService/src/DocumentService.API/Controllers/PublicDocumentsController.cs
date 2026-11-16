using System.Security.Claims;
using DocumentService.API.Services;
using DocumentService.Core.Common;
using DocumentService.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentService.API.Controllers;

[ApiController]
public sealed class PublicDocumentsController(PublicDocumentLinks links, IDocumentService documents,
    IFileStorageService storage) : ControllerBase
{
    [Authorize(Roles = "Clerk,Admin")]
    [HttpPost("api/documents/{id:guid}/public-link")]
    public async Task<IActionResult> Create(Guid id, CancellationToken token)
    {
        await documents.GetDocumentByIdAsync(id);
        var actor = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        var url = await links.CreateAsync(id, actor, token);
        Response.Headers.CacheControl = "no-store";
        return Ok(ApiResponse<object>.Ok(new { url }));
    }

    [Authorize(Roles = "Clerk,Admin")]
    [HttpDelete("api/documents/{id:guid}/public-link")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken token)
    {
        await documents.GetDocumentByIdAsync(id);
        await links.RevokeAsync(id, token);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("api/public/documents/{id:guid}")]
    public async Task<IActionResult> Read(Guid id, [FromQuery] string? key, [FromQuery] bool download,
        CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        var file = await links.FindAsync(id, key, token);
        if (file == null) return NotFound();
        var path = file.Value.Path;
        if (path.StartsWith("documents/", StringComparison.Ordinal)) path = path[10..];
        var stream = await storage.DownloadFileAsync(path, "documents");
        if (download) return File(stream, "application/pdf", file.Value.FileName, enableRangeProcessing: true);
        Response.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("inline")
        { FileNameStar = file.Value.FileName }.ToString();
        return File(stream, "application/pdf", enableRangeProcessing: true);
    }
}
