namespace IdentityService.Core.Entities;

/// <summary>
/// JWT access token đã bị thu hồi theo claim jti.
/// Dùng cho logout và validate-token tập trung của IdentityService.
/// </summary>
public class RevokedAccessToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Jti { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime RevokedAt { get; set; } = DateTime.UtcNow;

    public AppUser User { get; set; } = null!;
}
