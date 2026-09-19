using Microsoft.EntityFrameworkCore;

namespace IdentityService.Infrastructure.Data;

public static class AuthStoreInitializer
{
    /// <summary>
    /// IdentityService hiện dùng EnsureCreatedAsync(), nên DB đã tồn tại sẽ không tự thêm bảng mới.
    /// Hàm này tạo bổ sung bảng auth-session nếu chưa có trong PostgreSQL Docker/local.
    /// </summary>
    public static async Task EnsureAuthTablesAsync(AppDbContext context)
    {
        if (context.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true)
            return;

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "RefreshTokens" (
                "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                "UserId" uuid NOT NULL,
                "TokenHash" character varying(64) NOT NULL,
                "AccessTokenJti" character varying(64) NOT NULL,
                "ExpiresAt" timestamp with time zone NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC'),
                "RevokedAt" timestamp with time zone NULL,
                "ReplacedByTokenHash" character varying(64) NULL,
                CONSTRAINT "FK_RefreshTokens_AppUsers_UserId"
                    FOREIGN KEY ("UserId") REFERENCES "AppUsers" ("Id") ON DELETE CASCADE
            );
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_RefreshTokens_TokenHash"
                ON "RefreshTokens" ("TokenHash");
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_RefreshTokens_UserId_RevokedAt_ExpiresAt"
                ON "RefreshTokens" ("UserId", "RevokedAt", "ExpiresAt");
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "RevokedAccessTokens" (
                "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                "UserId" uuid NOT NULL,
                "Jti" character varying(64) NOT NULL,
                "ExpiresAt" timestamp with time zone NOT NULL,
                "RevokedAt" timestamp with time zone NOT NULL DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC'),
                CONSTRAINT "FK_RevokedAccessTokens_AppUsers_UserId"
                    FOREIGN KEY ("UserId") REFERENCES "AppUsers" ("Id") ON DELETE CASCADE
            );
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_RevokedAccessTokens_Jti"
                ON "RevokedAccessTokens" ("Jti");
            """);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_RevokedAccessTokens_ExpiresAt"
                ON "RevokedAccessTokens" ("ExpiresAt");
            """);
    }
}
