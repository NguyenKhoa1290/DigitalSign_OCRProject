using IdentityService.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Infrastructure.Data;

/// <summary>
/// EF Core database context cho IdentityService.
/// Quản lý các entity: AppUser, AppRole, AppUserRole, Department.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AppRole> AppRoles => Set<AppRole>();
    public DbSet<AppUserRole> AppUserRoles => Set<AppUserRole>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<RevokedAccessToken> RevokedAccessTokens => Set<RevokedAccessToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── AppUser ──────────────────────────────────────────────────────────
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("AppUsers");
            entity.HasKey(u => u.Id);

            entity.Property(u => u.Id)
                  .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(u => u.Username)
                  .IsRequired()
                  .HasMaxLength(50);

            entity.Property(u => u.PasswordHash)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(u => u.FullName)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.Property(u => u.Email)
                  .HasMaxLength(100);

            entity.Property(u => u.EmailVerifiedAt);

            entity.Property(u => u.PhoneNumber)
                  .HasMaxLength(15);

            entity.Property(u => u.IsActive)
                  .HasDefaultValue(true);

            entity.Property(u => u.MustChangePassword)
                  .HasDefaultValue(false);

            entity.Property(u => u.CreatedAt)
                  .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");

            // Unique constraints
            entity.HasIndex(u => u.Username)
                  .IsUnique()
                  .HasDatabaseName("IX_AppUsers_Username");

            entity.HasIndex(u => u.Email)
                  .IsUnique()
                  .HasFilter("\"Email\" IS NOT NULL")
                  .HasDatabaseName("IX_AppUsers_Email");

            // Relationship: AppUser → Department (nullable FK)
            entity.HasOne(u => u.Department)
                  .WithMany(d => d.Users)
                  .HasForeignKey(u => u.DepartmentId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ── AppRole ──────────────────────────────────────────────────────────
        modelBuilder.Entity<AppRole>(entity =>
        {
            entity.ToTable("AppRoles");
            entity.HasKey(r => r.Id);

            entity.Property(r => r.Id)
                  .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(r => r.RoleName)
                  .IsRequired()
                  .HasMaxLength(50);

            entity.Property(r => r.Description)
                  .HasMaxLength(255);

            entity.HasIndex(r => r.RoleName)
                  .IsUnique()
                  .HasDatabaseName("IX_AppRoles_RoleName");
        });

        // ── AppUserRole (Junction) ────────────────────────────────────────────
        modelBuilder.Entity<AppUserRole>(entity =>
        {
            entity.ToTable("AppUserRoles");

            // Composite primary key
            entity.HasKey(ur => new { ur.UserId, ur.RoleId });

            entity.HasOne(ur => ur.User)
                  .WithMany(u => u.UserRoles)
                  .HasForeignKey(ur => ur.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ur => ur.Role)
                  .WithMany(r => r.UserRoles)
                  .HasForeignKey(ur => ur.RoleId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Department ────────────────────────────────────────────────────────
        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("Departments");
            entity.HasKey(d => d.Id);

            entity.Property(d => d.Id)
                  .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(d => d.DeptName)
                  .IsRequired()
                  .HasMaxLength(150);

            entity.Property(d => d.DeptCode)
                  .IsRequired()
                  .HasMaxLength(20);

            entity.Property(d => d.Description)
                  .HasMaxLength(500);

            entity.Property(d => d.CreatedAt)
                  .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");

            entity.HasIndex(d => d.DeptCode)
                  .IsUnique()
                  .HasDatabaseName("IX_Departments_DeptCode");

            // Self-referencing hierarchy
            entity.HasOne(d => d.Parent)
                  .WithMany(d => d.Children)
                  .HasForeignKey(d => d.ParentId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PasswordResetToken ─────────────────────────────────────────────────
        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.ToTable("PasswordResetTokens");
            entity.HasKey(t => t.Id);

            entity.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
            entity.Property(t => t.IsUsed).HasDefaultValue(false);
            entity.Property(t => t.CreatedAt)
                  .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");

            entity.HasIndex(t => new { t.UserId, t.IsUsed })
                  .HasDatabaseName("IX_PasswordResetTokens_UserId_IsUsed");

            entity.HasOne(t => t.User)
                  .WithMany()
                  .HasForeignKey(t => t.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── EmailVerificationToken ────────────────────────────────────────────
        modelBuilder.Entity<EmailVerificationToken>(entity =>
        {
            entity.ToTable("EmailVerificationTokens");
            entity.HasKey(t => t.Id);

            entity.Property(t => t.Email).IsRequired().HasMaxLength(100);
            entity.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
            entity.Property(t => t.IsUsed).HasDefaultValue(false);
            entity.Property(t => t.CreatedAt)
                  .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");

            entity.HasIndex(t => new { t.UserId, t.IsUsed })
                  .HasDatabaseName("IX_EmailVerificationTokens_UserId_IsUsed");

            entity.HasOne(t => t.User)
                  .WithMany()
                  .HasForeignKey(t => t.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── RefreshToken ─────────────────────────────────────────────────────
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(t => t.Id);

            entity.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
            entity.Property(t => t.AccessTokenJti).IsRequired().HasMaxLength(64);
            entity.Property(t => t.ReplacedByTokenHash).HasMaxLength(64);
            entity.Property(t => t.CreatedAt)
                  .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");

            entity.HasIndex(t => t.TokenHash)
                  .IsUnique()
                  .HasDatabaseName("IX_RefreshTokens_TokenHash");

            entity.HasIndex(t => new { t.UserId, t.RevokedAt, t.ExpiresAt })
                  .HasDatabaseName("IX_RefreshTokens_UserId_RevokedAt_ExpiresAt");

            entity.HasOne(t => t.User)
                  .WithMany()
                  .HasForeignKey(t => t.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── RevokedAccessToken ───────────────────────────────────────────────
        modelBuilder.Entity<RevokedAccessToken>(entity =>
        {
            entity.ToTable("RevokedAccessTokens");
            entity.HasKey(t => t.Id);

            entity.Property(t => t.Jti).IsRequired().HasMaxLength(64);
            entity.Property(t => t.RevokedAt)
                  .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");

            entity.HasIndex(t => t.Jti)
                  .IsUnique()
                  .HasDatabaseName("IX_RevokedAccessTokens_Jti");

            entity.HasIndex(t => t.ExpiresAt)
                  .HasDatabaseName("IX_RevokedAccessTokens_ExpiresAt");

            entity.HasOne(t => t.User)
                  .WithMany()
                  .HasForeignKey(t => t.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Seed Data ─────────────────────────────────────────────────────────
        SeedRoles(modelBuilder);
        SeedDepartments(modelBuilder);
        SeedAdminUser(modelBuilder);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Seed Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static void SeedRoles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppRole>().HasData(
            new AppRole
            {
                Id          = new Guid("11111111-0000-0000-0000-000000000001"),
                RoleName    = "Admin",
                Description = "Quản trị viên hệ thống, toàn quyền truy cập."
            },
            new AppRole
            {
                Id          = new Guid("11111111-0000-0000-0000-000000000002"),
                RoleName    = "Clerk",
                Description = "Chuyên viên văn thư, xử lý tài liệu hàng ngày."
            },
            new AppRole
            {
                Id          = new Guid("11111111-0000-0000-0000-000000000003"),
                RoleName    = "Specialist",
                Description = "Chuyên viên nghiệp vụ, xem xét và phê duyệt tài liệu."
            },
            new AppRole
            {
                Id          = new Guid("11111111-0000-0000-0000-000000000004"),
                RoleName    = "Manager",
                Description = "Trưởng/Phó phòng, quản lý đơn vị."
            },
            new AppRole
            {
                Id          = new Guid("11111111-0000-0000-0000-000000000005"),
                RoleName    = "BoardOfDirectors",
                Description = "Ban Giám hiệu, ký duyệt văn bản cấp cao."
            }
        );
    }

    private static void SeedDepartments(ModelBuilder modelBuilder)
    {
        var rootId  = new Guid("22222222-0000-0000-0000-000000000001");
        var childId = new Guid("22222222-0000-0000-0000-000000000002");

        modelBuilder.Entity<Department>().HasData(
            new Department
            {
                Id          = rootId,
                DeptName    = "Trường Đại học Kiến Trúc Hà Nội",
                DeptCode    = "HAU",
                ParentId    = null,
                Description = "Đơn vị chủ quản – Trường Đại học Kiến Trúc Hà Nội.",
                CreatedAt   = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Department
            {
                Id          = childId,
                DeptName    = "Phòng Tổng hợp",
                DeptCode    = "TH",
                ParentId    = rootId,
                Description = "Phòng Tổng hợp – Hành chính tổng hợp.",
                CreatedAt   = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }

    private static void SeedAdminUser(ModelBuilder modelBuilder)
    {
        var adminId      = new Guid("33333333-0000-0000-0000-000000000001");
        var adminRoleId  = new Guid("11111111-0000-0000-0000-000000000001");
        var deptId       = new Guid("22222222-0000-0000-0000-000000000001");

        // Pre-hashed password "Admin@123" using BCrypt (work factor 12)
        const string passwordHash =
            "$2a$12$vDy8tgWVFj8xLeDXrwyxoewFWeAkNednPie8O.11nNxsnWpeH2Qc.";

        modelBuilder.Entity<AppUser>().HasData(
            new AppUser
            {
                Id                 = adminId,
                Username           = "admin",
                PasswordHash       = passwordHash,
                FullName           = "System Administrator",
                Email              = "admin@hau.edu.vn",
                PhoneNumber        = null,
                DepartmentId       = deptId,
                IsActive           = true,
                MustChangePassword = false,   // Admin không cần đổi mật khẩu lần đầu
                CreatedAt          = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        modelBuilder.Entity<AppUserRole>().HasData(
            new AppUserRole
            {
                UserId = adminId,
                RoleId = adminRoleId
            }
        );
    }
}
