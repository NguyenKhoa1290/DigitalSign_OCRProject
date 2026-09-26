using IdentityService.Core.DTOs.Auth;
using IdentityService.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IdentityService.API.Controllers;

/// <summary>
/// Authentication endpoints: Login, Logout, Refresh Token, Validate Token,
/// Change Password, Forgot Password, Reset Password.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger      = logger;
    }

    /// <summary>Đăng nhập hệ thống. Trả về JWT + MustChangePassword flag.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        _logger.LogInformation("Login attempt for user: {Username}", request.Username);
        var response = await _authService.LoginAsync(request);
        _logger.LogInformation("Login successful for user: {Username} | MustChangePassword: {Flag}",
            request.Username, response.MustChangePassword);
        return Ok(response);
    }

    /// <summary>Làm mới Access Token bằng Refresh Token.</summary>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequestDto request)
    {
        var response = await _authService.RefreshTokenAsync(request);
        return Ok(response);
    }

    /// <summary>Kiểm tra tính hợp lệ của Token (dùng bởi API Gateway).</summary>
    [HttpPost("validate-token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ValidateTokenResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateToken([FromBody] ValidateTokenRequestDto request)
    {
        var response = await _authService.ValidateTokenAsync(request);
        return Ok(response);
    }

    /// <summary>Đăng xuất - vô hiệu hóa phiên làm việc hiện tại.</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst("nameid")?.Value;
        var accessToken = Request.Headers.Authorization
            .ToString()
            .Replace("Bearer ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (!string.IsNullOrEmpty(accessToken))
            await _authService.LogoutAsync(accessToken);

        _logger.LogInformation("User {UserId} logged out", userId);
        return Ok(new { message = "Đăng xuất thành công" });
    }

    /// <summary>Gửi OTP tới email cần xác minh trong lần đăng nhập đầu.</summary>
    [HttpPost("send-email-verification")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendEmailVerification([FromBody] SendEmailVerificationDto dto)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(new { message = "Không xác định được người dùng từ token." });

        await _authService.SendEmailVerificationAsync(userId, dto);
        return Ok(new { message = "Mã xác minh đã được gửi tới email." });
    }

    /// <summary>
    /// Đổi mật khẩu (dùng cho lần đầu đăng nhập bắt buộc lẫn đổi thông thường).
    /// Kèm cập nhật email/SĐT tùy chọn (để dùng khôi phục mật khẩu sau này).
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(new { message = "Không xác định được người dùng từ token." });

        await _authService.ChangePasswordAsync(userId, dto);
        _logger.LogInformation("User {UserId} changed password successfully", userId);
        return Ok(new { message = "Đổi mật khẩu thành công." });
    }

    /// <summary>
    /// Gửi OTP 6 chữ số về email để khôi phục mật khẩu.
    /// Luôn trả 200 OK dù email có tồn tại hay không (tránh user enumeration).
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        await _authService.ForgotPasswordAsync(dto);
        return Ok(new { message = "Nếu email tồn tại trong hệ thống, bạn sẽ nhận được mã OTP trong vài phút." });
    }

    /// <summary>
    /// Xác thực OTP và đặt lại mật khẩu mới.
    /// OTP hết hạn sau 15 phút hoặc sau khi đã dùng.
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        await _authService.ResetPasswordAsync(dto);
        _logger.LogInformation("Password reset successful for email: {Email}", dto.Email);
        return Ok(new { message = "Đặt lại mật khẩu thành công. Vui lòng đăng nhập lại." });
    }
}
