using DocumentService.Core.Common;
using DocumentService.Core.DTOs;
using DocumentService.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Net;
using System.Text.Json;

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
        var result = await _documentService.GetAllDocumentsAsync(query);
        return Ok(ApiResponse<PagedResult<DocumentDto>>.Ok(result));
    }

    /// <summary>
    /// POST /api/documents - Tạo văn bản mới (status = Draft)
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<DocumentDto>), 201)]
    public async Task<IActionResult> Create([FromBody] CreateDocumentDto dto)
    {
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
        var result = await _documentService.GetDocumentByIdAsync(id);
        return Ok(ApiResponse<DocumentDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Clerk,Specialist")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDocumentDto dto)
    {
        var result = await _documentService.UpdateDocumentAsync(id, dto, GetCurrentUserId());
        return Ok(ApiResponse<DocumentDto>.Ok(result, "Đã cập nhật công văn."));
    }

    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> GetFile(Guid id, [FromQuery] bool download = false)
    {
        var file = await _documentService.GetFileAsync(id);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return download
            ? File(file.Content, "application/pdf", file.FileName, enableRangeProcessing: true)
            : File(file.Content, "application/pdf", enableRangeProcessing: true);
    }

    /// <summary>
    /// DELETE /api/documents/{id} - Xóa văn bản (chỉ khi Draft hoặc Rejected)
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _documentService.DeleteDocumentAsync(id);
        return Ok(ApiResponse.Ok("Xóa văn bản thành công."));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // File Upload
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/documents/{id}/upload - Văn thư upload file PDF lên MinIO
    /// </summary>
    [HttpPost("{id:guid}/upload")]
    [ProducesResponseType(typeof(ApiResponse<DocumentDto>), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> UploadFile(Guid id, IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("Vui lòng chọn file để upload."));

        var userId = GetCurrentUserId();

        using var stream = file.OpenReadStream();
        var result = await _documentService.UploadFileAsync(
            id, stream, file.FileName, file.ContentType, userId);

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

        var result = await _documentService.UpdateOcrDataAsync(id, dto, userId);
        return Ok(ApiResponse<DocumentDto>.Ok(result, "Cập nhật OCR thành công."));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Workflow
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/documents/{id}/submit - Trình duyệt (Draft → PendingDeptReview)
    /// </summary>
    [HttpPost("{id:guid}/submit")]
    [ProducesResponseType(typeof(ApiResponse<DocumentDto>), 200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Submit(Guid id, [FromBody] WorkflowActionDto? dto)
    {
        var userId = GetCurrentUserId();
        var result = await _documentService.SubmitForReviewAsync(id, userId, dto?.Comment);
        return Ok(ApiResponse<DocumentDto>.Ok(result, "Đã trình duyệt văn bản."));
    }

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
    /// POST /api/documents/{id}/assign - Phân công xử lý văn bản
    /// </summary>
    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = "Admin,Clerk,Manager,BoardOfDirectors")]
    [ProducesResponseType(typeof(ApiResponse<DocumentDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Assign(Guid id, [FromBody] WorkflowActionDto dto)
    {
        if (dto.ToUserId is null || dto.ToUserId == Guid.Empty)
            return BadRequest(ApiResponse<object>.Fail("Vui lòng cung cấp ToUserId để phân công."));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/users/{dto.ToUserId}");
            request.Headers.TryAddWithoutValidation("Authorization", Request.Headers.Authorization.ToString());
            using var response = await _httpClientFactory.CreateClient("identity-directory")
                .SendAsync(request, HttpContext.RequestAborted);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return BadRequest(ApiResponse<object>.Fail("Người nhận không tồn tại."));
            if (response.StatusCode == HttpStatusCode.Unauthorized) return Unauthorized();
            if (!response.IsSuccessStatusCode)
                return StatusCode(503, ApiResponse<object>.Fail("Chưa xác nhận được người nhận. Vui lòng thử lại."));
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("data", out var user)
                || user.ValueKind != JsonValueKind.Object
                || !user.TryGetProperty("isActive", out var active))
                return StatusCode(503, ApiResponse<object>.Fail("Phản hồi danh bạ người dùng không hợp lệ."));
            if (active.ValueKind != JsonValueKind.True)
                return BadRequest(ApiResponse<object>.Fail("Tài khoản người nhận không hoạt động."));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Không kiểm tra được người nhận phân công {UserId}", dto.ToUserId);
            return StatusCode(503, ApiResponse<object>.Fail("Chưa kết nối được danh bạ người dùng. Vui lòng thử lại."));
        }

        var userId = GetCurrentUserId();
        var result = await _documentService.AssignAsync(id, userId, dto.ToUserId.Value, dto.Comment);
        return Ok(ApiResponse<DocumentDto>.Ok(result, "Đã phân công xử lý văn bản."));
    }
}
