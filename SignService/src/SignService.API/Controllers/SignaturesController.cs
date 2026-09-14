using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SignService.Core.Common;
using SignService.Core.DTOs;
using SignService.Core.Interfaces;

namespace SignService.API.Controllers;

[ApiController]
[Route("api/signatures")]
[Authorize]
public class SignaturesController : ControllerBase
{
    private readonly ISignService _signService;
    private readonly ILogger<SignaturesController> _logger;

    public SignaturesController(ISignService signService, ILogger<SignaturesController> logger)
    {
        _signService = signService;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // POST api/signatures/personal-sign
    // Ký nháy — Lãnh đạo Phòng
    // ──────────────────────────────────────────────────────────────────────────
    [HttpPost("personal-sign")]
    [Authorize(Roles = "Manager,Admin")]
    [ProducesResponseType(typeof(ApiResponse<SignResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PersonalSign([FromBody] SignRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<SignResultDto>.Fail(
                ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage))));

        var result = await _signService.PersonalSignAsync(request);
        return Ok(ApiResponse<SignResultDto>.Ok(result, "Ký nháy thành công."));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // POST api/signatures/legal-seal
    // Ký pháp nhân — Ban Giám hiệu
    // ──────────────────────────────────────────────────────────────────────────
    [HttpPost("legal-seal")]
    [Authorize(Roles = "BoardOfDirectors,Admin")]
    [ProducesResponseType(typeof(ApiResponse<SignResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LegalSeal([FromBody] SignRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<SignResultDto>.Fail(
                ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage))));

        var result = await _signService.LegalSealAsync(request);
        return Ok(ApiResponse<SignResultDto>.Ok(result, "Ký pháp nhân thành công."));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET api/signatures/document/{docId}
    // Lấy danh sách chữ ký của văn bản
    // ──────────────────────────────────────────────────────────────────────────
    [HttpGet("document/{docId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<SignatureDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByDocument([FromRoute] Guid docId)
    {
        var signatures = await _signService.GetSignaturesByDocumentAsync(docId);
        return Ok(ApiResponse<IEnumerable<SignatureDto>>.Ok(signatures));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET api/signatures/document/{docId}/verify
    // Xác minh chữ ký trên PDF
    // ──────────────────────────────────────────────────────────────────────────
    [HttpGet("document/{docId:guid}/verify")]
    [ProducesResponseType(typeof(ApiResponse<VerifyResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifySignatures([FromRoute] Guid docId)
    {
        var result = await _signService.VerifySignaturesAsync(docId);
        return Ok(ApiResponse<VerifyResultDto>.Ok(result));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // POST api/signatures/certificates/issue
    // Cấp certificate cho user — Admin only
    // ──────────────────────────────────────────────────────────────────────────
    [HttpPost("certificates/issue")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<CertificateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IssueCertificate([FromBody] IssueCertificateDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<CertificateDto>.Fail(
                ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage))));

        var result = await _signService.IssueCertificateAsync(request);
        return Ok(ApiResponse<CertificateDto>.Ok(result, "Cấp chứng thư số thành công."));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET api/signatures/certificates/{userId}
    // Lấy thông tin certificate — Admin hoặc chính user đó
    // ──────────────────────────────────────────────────────────────────────────
    [HttpGet("certificates/{userId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<CertificateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCertificate([FromRoute] Guid userId)
    {
        // Chỉ Admin hoặc chính user đó mới xem được
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("sub")?.Value;
        bool isAdmin = User.IsInRole("Admin");

        if (!isAdmin && currentUserId != userId.ToString())
            return Forbid();

        var cert = await _signService.GetCertificateAsync(userId);
        if (cert == null)
            return NotFound(ApiResponse<CertificateDto>.Fail($"Người dùng {userId} chưa có chứng thư số."));

        return Ok(ApiResponse<CertificateDto>.Ok(cert));
    }
}
