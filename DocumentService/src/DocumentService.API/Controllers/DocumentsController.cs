using DocumentService.Core.Common;
using DocumentService.Core.DTOs;
using DocumentService.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using DocumentService.Core.Entities;
using DocumentService.Core.Exceptions;

namespace DocumentService.API.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private const string OcrServiceTokenHeader = "X-Service-Token";

    private readonly IDocumentService _documentService;
    private readonly ILogger<DocumentsController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public DocumentsController(
        IDocumentService documentService,
        ILogger<DocumentsController> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _documentService = documentService;
        _logger = logger;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    // ── Lấy userId từ JWT claim ───────────────────────────────────────────────
    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub")
               ?? throw new UnauthorizedAccessException("Không tìm thấy UserId trong token.");
        return Guid.Parse(sub);
    }

    private DocumentActor CurrentActor() => new(GetCurrentUserId(),
        User.FindAll(ClaimTypes.Role).Concat(User.FindAll("role")).Select(c => c.Value).Distinct().ToArray());

    private bool TryGetOcrCallerUserId(out Guid userId, out string? errorMessage)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            userId = GetCurrentUserId();
            errorMessage = null;
            return true;
        }

        var expectedToken = _configuration["ServiceAuth:OcrServiceToken"];
        var providedToken = Request.Headers[OcrServiceTokenHeader].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(expectedToken)
            || !string.Equals(providedToken, expectedToken, StringComparison.Ordinal))
        {
            userId = Guid.Empty;
            errorMessage = "OCR service token không hợp lệ.";
            return false;
        }

        var serviceUserId = _configuration["ServiceAuth:OcrServiceUserId"];
        if (!Guid.TryParse(serviceUserId, out userId))
        {
            throw new InvalidOperationException("ServiceAuth:OcrServiceUserId chưa được cấu hình đúng GUID.");
        }

        errorMessage = null;
        return true;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CRUD
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/documents - Lấy danh sách văn bản (có phân trang, lọc)
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<DocumentDto>>), 200)]
    public async Task<IActionResult> GetAll([FromQuery] DocumentQueryParams query)
    {
        query.SigningView = null; // Internal signing parameters are never accepted on the general list.
        Response.Headers.CacheControl = "no-store";
        var result = await _documentService.GetAllDocumentsAsync(query);
        return Ok(ApiResponse<PagedResult<DocumentDto>>.Ok(result));
    }

    [HttpGet("signing"), Authorize(Roles = "Admin,Manager,BoardOfDirectors")]
    public async Task<IActionResult> GetSigning([FromQuery] DocumentQueryParams query, [FromQuery] string view = "pending")
    {
        if (view is not ("pending" or "signed")) return BadRequest(ApiResponse<object>.Fail("Chọn danh sách chờ ký hoặc đã ký."));
        var db = HttpContext.RequestServices.GetRequiredService<DocumentService.Infrastructure.Data.AppDbContext>();
        var access = db.AccessScope ?? throw new UnauthorizedDocumentAccessException();
        query.SigningView = view; query.SigningUserId = access.UserId;
        query.SigningRoles = access.Roles.ToArray(); query.SigningDepartmentId = access.DepartmentId;
        Response.Headers.CacheControl = "no-store";
        return Ok(ApiResponse<PagedResult<DocumentDto>>.Ok(await _documentService.GetAllDocumentsAsync(query)));
    }

    /// <summary>
    /// POST /api/documents - Tạo văn bản mới (status = Draft)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Clerk,Specialist")]
    [ProducesResponseType(typeof(ApiResponse<DocumentDto>), 201)]
    public async Task<IActionResult> Create([FromBody] CreateDocumentDto dto)
    {
        if (!User.IsInRole("Admin"))
        {
            var type = (await _documentService.GetDocumentTypesAsync()).FirstOrDefault(t => t.Id == dto.DocTypeId);
            if (type == null) return BadRequest(ApiResponse<object>.Fail("Loại văn bản không tồn tại."));
            if (type.TypeName == "CongVanDen" ? !User.IsInRole("Clerk") : !User.IsInRole("Specialist")) return Forbid();
        }
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<object>.Fail(
                ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage))));

        var userId = GetCurrentUserId();
        var result = await _documentService.CreateDocumentAsync(dto, userId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id },
            ApiResponse<DocumentDto>.Ok(result, "Tạo văn bản thành công."));
    }

    /// <summary>
    /// GET /api/documents/types - Lấy danh sách loại văn bản
    /// </summary>
    [HttpGet("types")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<DocumentTypeDto>>), 200)]
    public async Task<IActionResult> GetTypes()
    {
        var result = await _documentService.GetDocumentTypesAsync();
        return Ok(ApiResponse<IEnumerable<DocumentTypeDto>>.Ok(result));
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStatistics()
    {
        Response.Headers.CacheControl = "no-store";
        var statistics = await _documentService.GetStatisticsAsync(GetCurrentUserId());
        return Ok(ApiResponse<DocumentStatisticsDto>.Ok(statistics));
    }

    /// <summary>
    /// GET /api/documents/{id} - Lấy chi tiết một văn bản (kèm processes)
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<DocumentDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(Guid id)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await _documentService.GetDocumentByIdAsync(id);
        return Ok(ApiResponse<DocumentDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Clerk,Specialist")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDocumentDto dto)
    {
        if (!User.IsInRole("Admin"))
        {
            var current = await _documentService.GetDocumentByIdAsync(id);
            if (!DocumentWorkflowAccess.CanAuthor(current.DocTypeName, current.CreatedById, CurrentActor())) return Forbid();
            var target = (await _documentService.GetDocumentTypesAsync()).FirstOrDefault(t => t.Id == dto.DocTypeId);
            if (target == null) return BadRequest(ApiResponse<object>.Fail("Loại văn bản không tồn tại."));
            var incoming = current.DocTypeName == "CongVanDen";
            if (incoming != (target.TypeName == "CongVanDen") || (incoming && !User.IsInRole("Clerk"))) return Forbid();
        }
        var result = await _documentService.UpdateDocumentAsync(id, dto, CurrentActor());
        return Ok(ApiResponse<DocumentDto>.Ok(result, "Đã cập nhật công văn."));
    }

    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> GetFile(Guid id, [FromQuery] bool download = false)
    {
        var file = await _documentService.GetFileAsync(id);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return download
            ? File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true)
            : File(file.Content, file.ContentType, enableRangeProcessing: true);
    }

    /// <summary>
    /// DELETE /api/documents/{id} - Xóa mềm bản nháp hợp lệ
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,Clerk,Specialist")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(Guid id, [FromBody] DocumentDeletionDto? dto = null)
    {
        await _documentService.DeleteDocumentAsync(id, CurrentActor(), dto?.Reason);
        return Ok(ApiResponse.Ok("Đã xóa mềm bản nháp."));
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelDocumentDto dto)
    {
        await _documentService.CancelDocumentAsync(id, CurrentActor(), dto.Reason, dto.ExpectedVersion!.Value);
        return Ok(ApiResponse.Ok("Đã hủy công văn. Tệp và lịch sử được giữ lại."));
    }

    [HttpGet("deleted")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetDeleted([FromQuery] DocumentQueryParams query)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(ApiResponse<PagedResult<DocumentDto>>.Ok(await _documentService.GetDeletedDocumentsAsync(query, CurrentActor())));
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Restore(Guid id, [FromBody] DocumentDeletionDto dto)
    {
        await _documentService.RestoreDocumentAsync(id, CurrentActor(), dto.Reason);
        return Ok(ApiResponse.Ok("Đã khôi phục bản nháp."));
    }

    [HttpPost("{id:guid}/receive")]
    [Authorize(Roles = "Admin,Clerk")]
    public async Task<IActionResult> Receive(Guid id)
        => Ok(ApiResponse<DocumentDto>.Ok(await _documentService.ReceiveDocumentAsync(id, CurrentActor()), "Đã tiếp nhận chính thức công văn đến."));

    // ──────────────────────────────────────────────────────────────────────────
    // File Upload
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/documents/{id}/upload - Văn thư upload file PDF lên MinIO
    /// </summary>
    [HttpPost("{id:guid}/upload")]
    [Authorize(Roles = "Admin,Clerk,Specialist")]
    [RequestSizeLimit(64L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 64L * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<DocumentDto>), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> UploadFile(Guid id, IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("Vui lòng chọn file để upload."));

        if (file.Length > 50L * 1024 * 1024)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                ApiResponse<object>.Fail("File vượt quá giới hạn 50 MB."));

        var document = await _documentService.GetDocumentByIdAsync(id);
        if (!DocumentWorkflowAccess.CanAuthor(document.DocTypeName, document.CreatedById, CurrentActor())) return Forbid();

        using var stream = file.OpenReadStream();
        var result = await _documentService.UploadFileAsync(
            id, stream, file.FileName, file.ContentType, CurrentActor());

        return Ok(ApiResponse<DocumentDto>.Ok(result, "Upload file thành công."));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // OCR
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// PATCH /api/documents/{id}/ocr - Cập nhật kết quả OCR (doc_number, title, issued_date, ocr_data_raw)
    /// </summary>
    [HttpPatch("{id:guid}/ocr")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<DocumentDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> UpdateOcr(Guid id, [FromBody] UpdateOcrDto dto)
    {
        if (!TryGetOcrCallerUserId(out var userId, out var errorMessage))
            return Unauthorized(ApiResponse<object>.Fail(errorMessage!));

        var document = await _documentService.GetDocumentByIdAsync(id);
        if (User.Identity?.IsAuthenticated == true
            ? !DocumentWorkflowAccess.CanOcr(document.Status, CurrentActor())
            : document.DocTypeName != "CongVanDen") return Forbid();

        if (document.MinioPath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<object>.Fail("Word là văn bản hoàn chỉnh, không cần OCR."));
        if (!User.IsInRole("Admin") && document.DocTypeName != "CongVanDen"
            && (dto.Title != null || dto.DocNumber != null || dto.IssuedDate.HasValue))
            return Forbid(); // OCR support does not grant authorship of an outgoing draft.

        var result = await _documentService.UpdateOcrDataAsync(id, dto, userId);
        return Ok(ApiResponse<DocumentDto>.Ok(result, "Cập nhật OCR thành công."));
    }

    [HttpGet("{id:guid}/ocr-state")]
    [AllowAnonymous]
    public async Task<IActionResult> OcrState(Guid id)
    {
        if (!TryGetOcrCallerUserId(out _, out var errorMessage))
            return Unauthorized(ApiResponse<object>.Fail(errorMessage!));
        Response.Headers.CacheControl = "no-store";
        var document = await _documentService.GetDocumentByIdAsync(id);
        if (User.Identity?.IsAuthenticated == true && !DocumentWorkflowAccess.CanOcr(document.Status, CurrentActor())) return Forbid();
        if (document.MinioPath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<object>.Fail("Word là văn bản hoàn chỉnh, không cần OCR."));
        return Ok(ApiResponse<object>.Ok(new { document.MinioPath, document.ConcurrencyVersion, document.DocTypeName }));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Workflow
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/documents/{id}/submit - Trình duyệt (Draft → PendingDeptReview)
    /// </summary>
    [HttpPost("{id:guid}/submit")]
    [Authorize(Roles = "Admin,Specialist")]
    [ProducesResponseType(typeof(ApiResponse<DocumentDto>), 200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Submit(Guid id, [FromBody] WorkflowActionDto? dto)
    {
        var userId = GetCurrentUserId();
        var document = await _documentService.GetDocumentByIdAsync(id);
        if (!User.IsInRole("Admin") && document.CreatedById != userId) return Forbid();
        var result = await _documentService.SubmitForReviewAsync(id, userId, dto?.Comment);
        return Ok(ApiResponse<DocumentDto>.Ok(result, "Đã trình duyệt văn bản."));
    }

    [HttpPost("{id:guid}/incoming-submit")]
    [Authorize(Roles = "Clerk,Admin")]
    public async Task<IActionResult> SubmitIncoming(Guid id, [FromBody] WorkflowActionDto dto)
        => Ok(ApiResponse<DocumentDto>.Ok(await _documentService.SubmitIncomingAsync(id, CurrentActor(), dto.ExpectedVersion, dto.Comment),
            "Đã trình Ban Giám hiệu ký chỉ đạo công văn đến."));

    /// <summary>
    /// POST /api/documents/{id}/dept-sign - Lãnh đạo phòng ký nháy (PendingDeptReview → DeptSigned)
    /// </summary>
    [HttpPost("{id:guid}/dept-sign")]
    [ProducesResponseType(typeof(ApiResponse<DocumentDto>), 200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> DeptSign(Guid id, [FromBody] WorkflowActionDto? dto)
    {
        var userId = GetCurrentUserId();
        var result = await _documentService.DeptSignAsync(id, userId, dto?.Comment);
        return Ok(ApiResponse<DocumentDto>.Ok(result, "Lãnh đạo phòng đã ký nháy."));
    }

    /// <summary>
    /// POST /api/documents/{id}/submit-director - Trình BGH ký (DeptSigned → PendingDirectorSign)
    /// </summary>
    [HttpPost("{id:guid}/submit-director")]
    [ProducesResponseType(typeof(ApiResponse<DocumentDto>), 200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> SubmitDirector(Guid id, [FromBody] WorkflowActionDto? dto)
    {
        var userId = GetCurrentUserId();
        var result = await _documentService.SubmitToDirectorAsync(id, userId, dto?.Comment);
        return Ok(ApiResponse<DocumentDto>.Ok(result, "Đã trình Ban Giám hiệu ký văn bản."));
    }

    /// <summary>
    /// POST /api/documents/{id}/director-sign - BGH ký số (PendingDirectorSign → DirectorSigned)
    /// </summary>
    [HttpPost("{id:guid}/director-sign")]
    [ProducesResponseType(typeof(ApiResponse<DocumentDto>), 200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> DirectorSign(Guid id, [FromBody] WorkflowActionDto? dto)
    {
        var userId = GetCurrentUserId();
        var result = await _documentService.DirectorSignAsync(id, userId, dto?.Comment);
        return Ok(ApiResponse<DocumentDto>.Ok(result, "Ban Giám hiệu đã ký số."));
    }

    /// <summary>
    /// POST /api/documents/{id}/reject - Từ chối (ở bất kỳ bước pending nào)
    /// </summary>
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(typeof(ApiResponse<DocumentDto>), 200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectDocumentDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<object>.Fail(
                ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage))));

        var userId = GetCurrentUserId();
        var result = await _documentService.RejectAsync(id, userId, dto.Reason);
        return Ok(ApiResponse<DocumentDto>.Ok(result, "Đã từ chối văn bản."));
    }

    /// <summary>
    /// POST /api/documents/{id}/publish - Văn thư phát hành (DirectorSigned → Published)
    /// </summary>
    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(typeof(ApiResponse<DocumentDto>), 200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Publish(Guid id)
    {
        var userId = GetCurrentUserId();
        var result = await _documentService.PublishAsync(id, userId);
        return Ok(ApiResponse<DocumentDto>.Ok(result, "Văn bản đã được phát hành."));
    }

    /// <summary>
    /// Legacy unsigned assignment is retired. Incoming assignment requires SignService.
    /// </summary>
    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = "Admin,Manager,BoardOfDirectors")]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public IActionResult Assign(Guid id, [FromBody] WorkflowActionDto dto)
        => StatusCode(StatusCodes.Status410Gone, ApiResponse<object>.Fail(
            "Công văn đi không có bước phân công xử lý. Công văn đến phải ký phân công sau khi BGH ký chỉ đạo."));
}
