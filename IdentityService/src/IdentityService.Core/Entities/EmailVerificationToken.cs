namespace IdentityService.Core.Entities;

/// <summary>
/// OTP xác minh quyền sở hữu email trước khi lưu email vào tài khoản.
/// </summary>
public class EmailVerificationToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual AppUser User { get; set; } = null!;
}
