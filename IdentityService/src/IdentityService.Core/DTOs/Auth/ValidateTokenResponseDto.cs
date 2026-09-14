namespace IdentityService.Core.DTOs.Auth;

/// <summary>
/// DTO trả về kết quả xác thực token cho các service khác trong hệ thống.
/// </summary>
public class ValidateTokenResponseDto
{
    /// <summary>Cho biết token có hợp lệ và còn hiệu lực hay không.</summary>
    public bool IsValid { get; set; }

    /// <summary>ID (GUID) của người dùng chủ sở hữu token. Null nếu token không hợp lệ.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Tên đăng nhập được trích xuất từ claims của token. Null nếu không hợp lệ.</summary>
    public string? Username { get; set; }

    /// <summary>Danh sách vai trò được trích xuất từ claims của token.</summary>
    public List<string> Roles { get; set; } = new List<string>();

    /// <summary>Thời điểm token hết hạn (UTC). Null nếu token không hợp lệ.</summary>
    public DateTime? ExpiresAt { get; set; }
}
