namespace IdentityService.Core.Entities;

/// <summary>
/// Bảng trung gian liên kết AppUser và AppRole (many-to-many).
/// Maps to table: AppUserRoles
/// Khóa chính kép (composite PK): UserId + RoleId
/// </summary>
public class AppUserRole
{
    /// <summary>Khóa ngoại tham chiếu đến AppUser (phần 1 của composite PK).</summary>
    public Guid UserId { get; set; }

    /// <summary>Khóa ngoại tham chiếu đến AppRole (phần 2 của composite PK).</summary>
    public Guid RoleId { get; set; }

    // -------------------------
    // Navigation Properties
    // -------------------------

    /// <summary>Người dùng được gán vai trò.</summary>
    public virtual AppUser User { get; set; } = null!;

    /// <summary>Vai trò được gán cho người dùng.</summary>
    public virtual AppRole Role { get; set; } = null!;
}
