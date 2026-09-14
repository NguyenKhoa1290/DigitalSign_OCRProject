# 🗄️ Cơ Sở Hạ Tầng & Cơ Sở Dữ Liệu

> Chi tiết cấu hình database, EF Core mapping, seed data, DI registration và connection strategy.

---

## I. Tổng Quan Hạ Tầng

| Thành phần | Công nghệ | Ghi chú |
|---|---|---|
| Database | PostgreSQL 15+ | Host: 192.168.50.10:5432 |
| ORM | Entity Framework Core 9 + Npgsql | Code-First |
| Schema strategy | EnsureCreatedAsync() | Không dùng Migration |
| Password hash | BCrypt.Net (work factor 12) | |
| JWT | System.IdentityModel.Tokens.Jwt | HS256 |
| Email | MailKit 4.8.0 + MimeKit 4.8.0 | Gmail SMTP |
| Object storage (OCR) | MinIO | Lưu file PDF |
| Message queue (OCR) | Apache Kafka | Topic: document.uploaded |

---

## II. AppDbContext.cs

**File:** `IdentityService.Infrastructure/Data/AppDbContext.cs`
**Base class:** `DbContext` (EF Core)

### DbSets

```csharp
DbSet<AppUser>             AppUsers
DbSet<AppRole>             AppRoles
DbSet<AppUserRole>         AppUserRoles
DbSet<Department>          Departments
DbSet<PasswordResetToken>  PasswordResetTokens
```

### OnModelCreating — Cấu hình từng Entity

---

#### AppUsers

| Column | Type | Constraint | Default |
|---|---|---|---|
| Id | UUID | PK | gen_random_uuid() |
| Username | VARCHAR(50) | NOT NULL | — |
| PasswordHash | VARCHAR(256) | NOT NULL | — |
| FullName | VARCHAR(100) | NOT NULL | — |
| Email | VARCHAR(100) | NULL | — |
| PhoneNumber | VARCHAR(15) | NULL | — |
| DepartmentId | UUID | NULL, FK → Departments | — |
| IsActive | BOOLEAN | NOT NULL | true |
| MustChangePassword | BOOLEAN | NOT NULL | false |
| CreatedAt | TIMESTAMP | NOT NULL | CURRENT_TIMESTAMP UTC |

**Indexes:**
```
IX_AppUsers_Username  → UNIQUE (Username)
IX_AppUsers_Email     → UNIQUE (Email) WHERE Email IS NOT NULL
```

**Foreign Keys:**
```
DepartmentId → Departments.Id   ON DELETE SET NULL
```

---

#### AppRoles

| Column | Type | Constraint | Default |
|---|---|---|---|
| Id | UUID | PK | gen_random_uuid() |
| RoleName | VARCHAR(50) | NOT NULL | — |
| Description | VARCHAR(255) | NULL | — |

**Indexes:**
```
IX_AppRoles_RoleName → UNIQUE (RoleName)
```

---

#### AppUserRoles (Junction Table)

| Column | Type | Constraint |
|---|---|---|
| UserId | UUID | PK (Composite), FK → AppUsers |
| RoleId | UUID | PK (Composite), FK → AppRoles |

**Primary Key:** Composite `(UserId, RoleId)`

**Foreign Keys:**
```
UserId → AppUsers.Id  ON DELETE CASCADE
RoleId → AppRoles.Id  ON DELETE CASCADE
```

---

#### Departments

| Column | Type | Constraint | Default |
|---|---|---|---|
| Id | UUID | PK | gen_random_uuid() |
| DeptName | VARCHAR(150) | NOT NULL | — |
| DeptCode | VARCHAR(20) | NOT NULL | — |
| ParentId | UUID | NULL, FK self-ref | — |
| Description | VARCHAR(500) | NULL | — |
| CreatedAt | TIMESTAMP | NOT NULL | CURRENT_TIMESTAMP UTC |

**Indexes:**
```
IX_Departments_DeptCode → UNIQUE (DeptCode)
```

**Foreign Keys (Self-referencing):**
```
ParentId → Departments.Id   ON DELETE RESTRICT
```
> `RESTRICT` thay vì `CASCADE` để tránh xóa cả cây khi xóa phòng ban cha.

---

#### PasswordResetTokens

| Column | Type | Constraint | Default |
|---|---|---|---|
| Id | UUID | PK | gen_random_uuid() |
| UserId | UUID | NOT NULL, FK → AppUsers | — |
| TokenHash | VARCHAR(64) | NOT NULL | — |
| ExpiresAt | TIMESTAMP | NOT NULL | — |
| IsUsed | BOOLEAN | NOT NULL | false |
| CreatedAt | TIMESTAMP | NOT NULL | CURRENT_TIMESTAMP UTC |

**Indexes:**
```
IX_PasswordResetTokens_UserId_IsUsed → (UserId, IsUsed)
```
> Index composite để query nhanh token hợp lệ theo userId.

**Foreign Keys:**
```
UserId → AppUsers.Id   ON DELETE CASCADE
```

---

## III. Seed Data

> Được gọi trong `OnModelCreating` qua `HasData()` — chỉ insert khi `EnsureCreated()` tạo DB lần đầu.

### Roles (5 records)

| Id | RoleName | Description |
|---|---|---|
| 11111111-0000-0000-0000-000000000001 | Admin | Quản trị viên hệ thống, toàn quyền truy cập |
| 11111111-0000-0000-0000-000000000002 | Clerk | Chuyên viên văn thư, xử lý tài liệu hàng ngày |
| 11111111-0000-0000-0000-000000000003 | Specialist | Chuyên viên nghiệp vụ, xem xét và phê duyệt |
| 11111111-0000-0000-0000-000000000004 | Manager | Trưởng/Phó phòng, quản lý đơn vị |
| 11111111-0000-0000-0000-000000000005 | BoardOfDirectors | Ban Giám hiệu, ký duyệt văn bản cấp cao |

### Departments (2 records)

| Id | DeptName | DeptCode | ParentId |
|---|---|---|---|
| 22222222-0000-0000-0000-000000000001 | Trường Đại học Kiến Trúc Hà Nội | HAU | NULL (root) |
| 22222222-0000-0000-0000-000000000002 | Phòng Tổng hợp | TH | 22222222-...001 |

### Admin User (1 record)

| Field | Value |
|---|---|
| Id | 33333333-0000-0000-0000-000000000001 |
| Username | admin |
| PasswordHash | BCrypt hash của "Admin@123" (work factor 12) |
| FullName | System Administrator |
| Email | admin@hau.edu.vn |
| DepartmentId | HAU (22222222-...001) |
| IsActive | true |
| MustChangePassword | false |

> Admin được gán role `Admin` qua `AppUserRoles` seed.

---

## IV. Chiến Lược Schema — EnsureCreated

```csharp
// Program.cs — chạy khi khởi động
using var scope = app.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
await db.Database.EnsureCreatedAsync();
```

**EnsureCreated hoạt động như sau:**
```
Lần đầu chạy:
  DB chưa tồn tại → Tạo tất cả bảng + indexes + constraints
  Chạy HasData() seed data → Insert roles, departments, admin

Lần sau:
  DB đã tồn tại → Không làm gì cả
```

> **Lưu ý quan trọng:** Khi thêm column/table mới vào entity sau khi DB đã tạo,
> EnsureCreated KHÔNG tự cập nhật schema → phải chạy SQL ALTER TABLE thủ công.
>
> **Ví dụ thực tế:** Thêm `MustChangePassword` vào `AppUser` và `PasswordResetTokens`:
> ```sql
> ALTER TABLE "AppUsers" ADD COLUMN IF NOT EXISTS "MustChangePassword" BOOLEAN NOT NULL DEFAULT false;
> CREATE TABLE IF NOT EXISTS "PasswordResetTokens" (...);
> ```
> **Tên cột phải dùng PascalCase có dấu `""` để khớp với EF Core khi PostgreSQL query.**

---

## V. DI Registration — ServiceCollectionExtensions

**File:** `IdentityService.Infrastructure/Extensions/ServiceCollectionExtensions.cs`

Gọi trong `Program.cs`:
```csharp
builder.Services.AddInfrastructure(builder.Configuration);
```

### DbContext

```csharp
services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions => {
        npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: 10s);
        npgsqlOptions.CommandTimeout(30);
    })
);
```

**Retry policy:** Tự động retry 5 lần nếu mất kết nối PostgreSQL, delay 10 giây giữa các lần.

### Repositories (Scoped)

| Interface | Implementation |
|---|---|
| `IUserRepository` | `UserRepository` |
| `IRoleRepository` | `RoleRepository` |
| `IDepartmentRepository` | `DepartmentRepository` |
| `IPasswordResetRepository` | `PasswordResetRepository` |

### Services (Scoped)

| Interface | Implementation |
|---|---|
| `ITokenService` | `TokenService` |
| `IAuthService` | `AuthService` |
| `IUserService` | `UserService` |
| `IRoleService` | `RoleService` |
| `IDepartmentService` | `DepartmentService` |
| `IEmailService` | `EmailService` |

> **Tất cả đều Scoped** — mỗi HTTP request tạo một instance mới, tự động dispose sau request.

---

## VI. Connection String

**appsettings.json (IdentityService):**
```json
"ConnectionStrings": {
  "DefaultConnection": "Host=192.168.50.10;Port=5432;Database=DigitalSign_OCR;Username=...;Password=..."
}
```

---

## VII. SQL Migration Thủ Công

Khi cần thêm column/table mà không muốn tạo lại DB, chạy trực tiếp trong pgAdmin:

```sql
-- Thêm cột MustChangePassword (nếu chưa có)
ALTER TABLE "AppUsers"
ADD COLUMN IF NOT EXISTS "MustChangePassword" BOOLEAN NOT NULL DEFAULT false;

-- Nếu đã tạo sai tên (lowercase), đổi tên:
ALTER TABLE "AppUsers" RENAME COLUMN must_change_password TO "MustChangePassword";

-- Tạo bảng PasswordResetTokens
CREATE TABLE IF NOT EXISTS "PasswordResetTokens" (
    "Id"        UUID        NOT NULL DEFAULT gen_random_uuid(),
    "UserId"    UUID        NOT NULL,
    "TokenHash" VARCHAR(64) NOT NULL,
    "ExpiresAt" TIMESTAMP   NOT NULL,
    "IsUsed"    BOOLEAN     NOT NULL DEFAULT false,
    "CreatedAt" TIMESTAMP   NOT NULL DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC'),
    CONSTRAINT "PK_PasswordResetTokens" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PasswordResetTokens_AppUsers"
        FOREIGN KEY ("UserId") REFERENCES "AppUsers"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_PasswordResetTokens_UserId_IsUsed"
    ON "PasswordResetTokens" ("UserId", "IsUsed");
```

> **Nguyên tắc:** Tên bảng/cột trong PostgreSQL phân biệt hoa/thường khi có dấu `""`.
> EF Core Npgsql mặc định dùng tên property C# nguyên bản (PascalCase) → luôn phải dùng dấu `""` khi viết SQL thủ công.

---

## VIII. Sơ Đồ Quan Hệ (ERD)

```
AppUsers ──────────────────── Departments
  Id (PK)                       Id (PK)
  Username (UNIQUE)              DeptName
  PasswordHash                   DeptCode (UNIQUE)
  FullName                       ParentId ──┐ (self-ref)
  Email (UNIQUE, nullable)       Description│
  PhoneNumber                    CreatedAt  │
  DepartmentId ──── FK ──────────┘          │
  IsActive                       Children ──┘
  MustChangePassword
  CreatedAt

AppUsers ──── AppUserRoles ──── AppRoles
  Id (PK)       UserId (FK,PK)    Id (PK)
                RoleId (FK,PK)    RoleName (UNIQUE)
                                  Description

AppUsers ──── PasswordResetTokens
  Id (PK)       Id (PK)
                UserId (FK)
                TokenHash (SHA-256)
                ExpiresAt
                IsUsed
                CreatedAt
```
