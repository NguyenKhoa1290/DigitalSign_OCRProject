namespace IdentityService.Core.DTOs.Auth;

/// <summary>
/// DTO trả về thông tin sau khi đăng nhập thành công.
/// </summary>
public class LoginResponseDto
{
    /// <summary>JWT Access Token dùng để xác thực các request tiếp theo.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Refresh Token dùng để gia hạn Access Token khi hết hạn.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Thời điểm Access Token hết hạn (UTC).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>ID (GUID) của người dùng đã đăng nhập.</summary>
    public Guid UserId { get; set; }

    /// <summary>Tên đăng nhập của người dùng.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Họ và tên đầy đủ của người dùng.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Danh sách tên các vai trò mà người dùng được gán.</summary>
    public List<string> Roles { get; set; } = new List<string>();

    /// <summary>Nếu true, frontend phải redirect đến trang /first-login để bắt buộc đổi mật khẩu.</summary>
    public bool MustChangePassword { get; set; } = false;
}
