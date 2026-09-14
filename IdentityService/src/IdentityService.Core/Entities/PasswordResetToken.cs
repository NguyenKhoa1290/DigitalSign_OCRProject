namespace IdentityService.Core.Entities;

/// <summary>
/// Token dùng để khôi phục mật khẩu qua email OTP.
/// Maps to table: PasswordResetTokens
/// </summary>
public class PasswordResetToken
{
    /// <summary>Khóa chính.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Khóa ngoại trỏ tới AppUser.</summary>
    public Guid UserId { get; set; }

    /// <summary>SHA-256 hash của OTP 6 chữ số gửi về email.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Thời điểm hết hạn (15 phút sau khi tạo).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Token đã được dùng hoặc bị vô hiệu hoá.</summary>
    public bool IsUsed { get; set; } = false;

    /// <summary>Thời điểm tạo.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual AppUser User { get; set; } = null!;
}
