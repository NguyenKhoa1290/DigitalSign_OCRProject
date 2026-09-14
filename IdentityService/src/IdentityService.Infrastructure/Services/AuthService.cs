using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IdentityService.Core.DTOs.Auth;
using IdentityService.Core.Entities;
using IdentityService.Core.Exceptions;
using IdentityService.Core.Interfaces;
using IdentityService.Core.Services;

namespace IdentityService.Infrastructure.Services;

/// <summary>
/// Triển khai IAuthService: xác thực, cấp phát JWT, đổi mật khẩu, khôi phục mật khẩu.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUserRepository         _userRepository;
    private readonly ITokenService           _tokenService;
    private readonly IPasswordResetRepository _resetRepository;
    private readonly IEmailService           _emailService;

    public AuthService(
        IUserRepository          userRepository,
        ITokenService            tokenService,
        IPasswordResetRepository resetRepository,
        IEmailService            emailService)
    {
        _userRepository  = userRepository;
        _tokenService    = tokenService;
        _resetRepository = resetRepository;
        _emailService    = emailService;
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request)
    {
        var user = await _userRepository.GetByUsernameAsync(request.Username)
            ?? throw new InvalidCredentialsException("Tên đăng nhập hoặc mật khẩu không đúng.");

        if (!user.IsActive)
            throw new AccountLockedException($"Tài khoản '{user.Username}' đã bị khoá, vui lòng liên hệ Quản trị viên.");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new InvalidCredentialsException("Tên đăng nhập hoặc mật khẩu không đúng.");

        var roles        = user.UserRoles.Select(ur => ur.Role.RoleName).ToList();
        var accessToken  = _tokenService.GenerateAccessToken(user, roles);
        var refreshToken = _tokenService.GenerateRefreshToken();

        return new LoginResponseDto
        {
            AccessToken        = accessToken,
            RefreshToken       = refreshToken,
            UserId             = user.Id,
            Username           = user.Username,
            FullName           = user.FullName,
            Roles              = roles,
            MustChangePassword = user.MustChangePassword
        };
    }

    // ── Refresh Token ─────────────────────────────────────────────────────────

    public async Task<LoginResponseDto> RefreshTokenAsync(RefreshTokenRequestDto request)
    {
        var tokenService = (TokenService)_tokenService;
        var principal = tokenService.GetPrincipalFromExpiredToken(request.AccessToken)
            ?? throw new TokenExpiredException("Access token không hợp lệ hoặc không thể phân tích.");

        var username = principal.Claims.FirstOrDefault(c => c.Type == "username")?.Value
            ?? throw new TokenExpiredException("Không tìm thấy username trong token.");

        var user = await _userRepository.GetByUsernameAsync(username)
            ?? throw new UserNotFoundException($"Không tìm thấy người dùng '{username}'.");

        if (!user.IsActive)
            throw new AccountLockedException($"Tài khoản '{user.Username}' đã bị khoá.");

        var roles           = user.UserRoles.Select(ur => ur.Role.RoleName).ToList();
        var newAccessToken  = _tokenService.GenerateAccessToken(user, roles);
        var newRefreshToken = _tokenService.GenerateRefreshToken();

        return new LoginResponseDto
        {
            AccessToken        = newAccessToken,
            RefreshToken       = newRefreshToken,
            UserId             = user.Id,
            Username           = user.Username,
            FullName           = user.FullName,
            Roles              = roles,
            MustChangePassword = user.MustChangePassword
        };
    }

    // ── Validate Token ────────────────────────────────────────────────────────

    public async Task<ValidateTokenResponseDto> ValidateTokenAsync(ValidateTokenRequestDto request)
    {
        var isValid = await _tokenService.ValidateTokenAsync(request.Token);
        if (!isValid)
            return new ValidateTokenResponseDto { IsValid = false, Roles = new List<string>() };

        try
        {
            var handler  = new JwtSecurityTokenHandler();
            var jwt      = handler.ReadJwtToken(request.Token);
            var userId   = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
            var username = jwt.Claims.FirstOrDefault(c => c.Type == "username")?.Value;
            var roles    = jwt.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();

            return new ValidateTokenResponseDto
            {
                IsValid   = true,
                UserId    = userId is not null ? Guid.Parse(userId) : null,
                Username  = username,
                Roles     = roles,
                ExpiresAt = jwt.ValidTo
            };
        }
        catch
        {
            return new ValidateTokenResponseDto { IsValid = false, Roles = new List<string>() };
        }
    }

    // ── Logout ────────────────────────────────────────────────────────────────

    public Task LogoutAsync(string userId)
    {
        // TODO: Thêm JTI vào blacklist (Redis / DB)
        return Task.CompletedTask;
    }

    // ── Change Password ───────────────────────────────────────────────────────

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordDto dto)
    {
        if (dto.NewPassword != dto.ConfirmPassword)
            throw new IdentityServiceException("Mật khẩu mới và xác nhận mật khẩu không khớp.", 400);

        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new UserNotFoundException(userId);

        // Xác thực mật khẩu hiện tại
        if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            throw new InvalidCredentialsException("Mật khẩu hiện tại không đúng.");

        // Không cho đặt trùng mật khẩu cũ
        if (BCrypt.Net.BCrypt.Verify(dto.NewPassword, user.PasswordHash))
            throw new IdentityServiceException("Mật khẩu mới không được trùng mật khẩu hiện tại.", 400);

        // Cập nhật mật khẩu
        user.PasswordHash       = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword, workFactor: 12);
        user.MustChangePassword = false;  // Đánh dấu đã đổi mật khẩu

        // Cập nhật email/SĐT nếu được cung cấp (lần đầu đăng nhập)
        if (!string.IsNullOrWhiteSpace(dto.Email))
            user.Email = dto.Email.Trim();
        if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
            user.PhoneNumber = dto.PhoneNumber.Trim();

        await _userRepository.UpdateAsync(user);
    }

    // ── Forgot Password ───────────────────────────────────────────────────────

    public async Task ForgotPasswordAsync(ForgotPasswordDto dto)
    {
        // Không báo lỗi nếu email không tồn tại → tránh user enumeration attack
        var user = await _userRepository.GetByEmailAsync(dto.Email);
        if (user is null || !user.IsActive) return;

        // Sinh OTP 6 chữ số ngẫu nhiên
        var otp     = GenerateOtp();
        var hash    = HashOtp(otp);
        var expires = DateTime.UtcNow.AddMinutes(15);

        // Lưu token (hash) vào DB
        await _resetRepository.CreateAsync(new PasswordResetToken
        {
            UserId    = user.Id,
            TokenHash = hash,
            ExpiresAt = expires
        });

        // Gửi OTP về email
        await _emailService.SendPasswordResetEmailAsync(dto.Email, user.FullName, otp);
    }

    // ── Reset Password ────────────────────────────────────────────────────────

    public async Task ResetPasswordAsync(ResetPasswordDto dto)
    {
        if (dto.NewPassword != dto.ConfirmPassword)
            throw new IdentityServiceException("Mật khẩu mới và xác nhận mật khẩu không khớp.", 400);

        var user = await _userRepository.GetByEmailAsync(dto.Email)
            ?? throw new IdentityServiceException("Email không hợp lệ hoặc không tồn tại.", 400);

        var hash  = HashOtp(dto.Otp);
        var token = await _resetRepository.GetValidTokenAsync(user.Id, hash);

        if (token is null)
            throw new IdentityServiceException("Mã OTP không hợp lệ hoặc đã hết hạn.", 400);

        // Không cho đặt trùng mật khẩu cũ
        if (BCrypt.Net.BCrypt.Verify(dto.NewPassword, user.PasswordHash))
            throw new IdentityServiceException("Mật khẩu mới không được trùng mật khẩu hiện tại.", 400);

        // Đặt mật khẩu mới
        user.PasswordHash       = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword, workFactor: 12);
        user.MustChangePassword = false;

        await _userRepository.UpdateAsync(user);

        // Vô hiệu hoá tất cả OTP của user này
        await _resetRepository.InvalidateAllAsync(user.Id);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Sinh OTP 6 chữ số an toàn mật mã (cryptographically secure).</summary>
    private static string GenerateOtp()
    {
        var number = RandomNumberGenerator.GetInt32(100000, 999999);
        return number.ToString();
    }

    /// <summary>SHA-256 hash của OTP — chỉ lưu hash vào DB, không lưu plain text.</summary>
    private static string HashOtp(string otp)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(otp));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
