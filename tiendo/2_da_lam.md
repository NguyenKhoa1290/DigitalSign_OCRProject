# 📋 Nhật Ký Công Việc — Những Gì Đã Làm

> Ghi lại toàn bộ các tính năng, sửa lỗi và cải tiến đã thực hiện trong dự án HAU DigitalSign OCR.

---

## 1. 🏗️ Xây Dựng IdentityService từ Đầu (Clean Architecture)

### Core Layer — `IdentityService.Core`

**Entities:**
- `AppUser.cs` — người dùng với đầy đủ fields + navigation properties
- `AppRole.cs` — vai trò
- `AppUserRole.cs` — junction table user ↔ role
- `Department.cs` — phòng ban, self-referencing tree (ParentId)
- `PasswordResetToken.cs` — token OTP khôi phục mật khẩu (thêm sau)

**DTOs:**
- Auth: LoginRequestDto, LoginResponseDto (có MustChangePassword), RefreshTokenRequestDto, ValidateTokenRequestDto/ResponseDto
- Auth (bổ sung): ChangePasswordDto, ForgotPasswordDto, ResetPasswordDto
- Users: CreateUserDto (RoleIds), UpdateUserDto, UserDto
- Departments: DepartmentDto (có Children recursive), CreateDepartmentDto
- Roles: RoleDto

**Interfaces:** IUserRepository, IRoleRepository, IDepartmentRepository, ITokenService, IPasswordResetRepository (thêm sau), IEmailService (thêm sau)

**Service interfaces:** IAuthService, IUserService, IRoleService, IDepartmentService

**Common:** ApiResponse<T> (static factory Ok/Fail), PagedResult<T>

**Exceptions:** IdentityServiceException (base), UserNotFoundException, InvalidCredentialsException, UserAlreadyExistsException, RoleNotFoundException, DepartmentNotFoundException, AccountLockedException, TokenExpiredException

---

### Infrastructure Layer — `IdentityService.Infrastructure`

**AppDbContext.cs:**
- Cấu hình EF Core với PostgreSQL (Npgsql)
- Composite PK cho AppUserRole
- Unique index cho Username, Email, DeptCode
- Seed data: 5 roles, 2 departments (HAU root + Phòng Tổng hợp), 1 admin user
- `EnsureCreatedAsync()` thay vì Migration (đơn giản cho môi trường dev)

**Repositories:**
- `UserRepository.cs` — GetByUsernameAsync include UserRoles.Role, phân trang tìm kiếm
- `RoleRepository.cs`
- `DepartmentRepository.cs` — GetChildrenAsync đệ quy
- `PasswordResetRepository.cs` — tạo/lấy/vô hiệu hóa OTP tokens

**Services:**
- `TokenService.cs` — JWT HS256, GenerateAccessToken (claims: sub, username, email, role, jti), GenerateRefreshToken (crypto random), ValidateTokenAsync, GetPrincipalFromExpiredToken
- `AuthService.cs` — Login, RefreshToken, ValidateToken, Logout (stub), ChangePassword, ForgotPassword (OTP), ResetPassword (verify OTP)
- `EmailService.cs` — Gmail SMTP via MailKit, HTML email template đẹp cho OTP
- `UserService.cs` — CRUD + BCrypt hash password
- `RoleService.cs`, `DepartmentService.cs`

**Extensions:**
- `ServiceCollectionExtensions.cs` — đăng ký toàn bộ DI (DbContext, Repositories, Services)

---

### API Layer — `IdentityService.API`

**Controllers:**
- `AuthController.cs` — 7 endpoints:
  - POST /api/auth/login
  - POST /api/auth/logout (Authorize)
  - POST /api/auth/refresh-token
  - POST /api/auth/validate-token
  - POST /api/auth/change-password (Authorize)
  - POST /api/auth/forgot-password (Anonymous)
  - POST /api/auth/reset-password (Anonymous)
- `UsersController.cs` — CRUD + assign/remove role
- `RolesController.cs`, `DepartmentsController.cs`

**Middleware:**
- `ExceptionHandlingMiddleware.cs` — bắt exception → map sang HTTP status:
  - UserNotFoundException → 404
  - InvalidCredentialsException → 401
  - UserAlreadyExistsException → 409
  - IdentityServiceException → 400
  - Others → 500

---

## 2. 🌳 Tính Năng Cây Phòng Ban

### Backend — `DepartmentService.cs`

- `GetDepartmentTreeAsync()` — trả về cấu trúc cây với `Children` đệ quy
- `HasCircularReferenceAsync()` — kiểm tra vòng lặp trước khi cập nhật ParentId (tránh A→B→A)

### Frontend — `Departments.razor`

- Hiển thị cây phân cấp (parent + children) đúng cấu trúc
- UI phòng ban hiện 2 dòng song song khi có quan hệ cha-con

---

## 3. 🔐 Luồng Onboarding & Khôi Phục Mật Khẩu

### Yêu cầu nghiệp vụ:
- Admin tạo user với mật khẩu tạm → user đăng nhập lần đầu bị bắt đổi mật khẩu
- User có thể thêm email/SĐT khi đổi mật khẩu lần đầu (dùng để khôi phục sau)
- Quên mật khẩu → OTP gửi qua email Gmail → nhập OTP + mật khẩu mới

### Những gì đã làm:

**Database (SQL migration thủ công — không dùng EF Migration):**
```sql
ALTER TABLE "AppUsers" ADD COLUMN IF NOT EXISTS "MustChangePassword" BOOLEAN NOT NULL DEFAULT false;

CREATE TABLE IF NOT EXISTS "PasswordResetTokens" (
    "Id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "UserId" UUID NOT NULL,
    "TokenHash" VARCHAR(64) NOT NULL,
    "ExpiresAt" TIMESTAMP NOT NULL,
    "IsUsed" BOOLEAN NOT NULL DEFAULT false,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC'),
    CONSTRAINT "PK_PasswordResetTokens" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PasswordResetTokens_AppUsers" FOREIGN KEY ("UserId") REFERENCES "AppUsers"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_PasswordResetTokens_UserId_IsUsed"
    ON "PasswordResetTokens" ("UserId", "IsUsed");
```

> Lưu ý: Tên cột phải dùng PascalCase có dấu ngoặc kép để khớp với EF Core (PostgreSQL phân biệt hoa/thường khi quoted).

**Backend bổ sung:**
- `AppUser.MustChangePassword` field
- `PasswordResetToken` entity
- `IEmailService`, `IPasswordResetRepository` interfaces
- `ChangePasswordDto`, `ForgotPasswordDto`, `ResetPasswordDto`
- `LoginResponseDto.MustChangePassword` field
- `EmailService.cs` — gửi OTP qua Gmail SMTP (MailKit + MimeKit)
- `PasswordResetRepository.cs` — CreateAsync, GetValidTokenAsync, InvalidateAllAsync
- `AuthService.cs` — 3 methods mới: ChangePasswordAsync, ForgotPasswordAsync, ResetPasswordAsync
  - OTP: `RandomNumberGenerator.GetInt32(100000, 999999)` — crypto secure
  - Lưu `SHA256(OTP)` vào DB, không lưu plain text
- `UserService.CreateUserAsync` — set `MustChangePassword = true` cho mọi user mới
- `appsettings.json` — thêm EmailSettings section (SmtpHost, SmtpPort, Username, Password, FromName)
- NuGet: thêm MailKit 4.8.0, MimeKit 4.8.0 vào Infrastructure.csproj

**Frontend bổ sung:**
- `LoginResponse.cs` — thêm `MustChangePassword` property
- `PasswordModels.cs` — ChangePasswordRequest, ForgotPasswordRequest, ResetPasswordRequest
- `AuthService.cs` — cập nhật `LoginAsync` trả `(bool Success, bool MustChangePassword, string? Error)`, thêm ChangePasswordAsync, ForgotPasswordAsync, ResetPasswordAsync
- `Login.razor` — thêm link "Quên mật khẩu?", redirect đến /first-login khi MustChangePassword=true
- `FirstLogin.razor` — trang đổi mật khẩu bắt buộc, có thanh đo độ mạnh password, ô nhập email/SĐT tùy chọn
- `ForgotPassword.razor` — nhập email, luôn trả thành công (tránh user enumeration)
- `ResetPassword.razor` — nhập OTP 6 số (font lớn) + mật khẩu mới

---

## 4. 🐛 Sửa Lỗi

### Lỗi build ban đầu (interface mismatch)
- **Vấn đề:** `UserRepository` có thêm tham số `CancellationToken` nhưng `IUserRepository` không có
- **Fix:** Bỏ `CancellationToken` khỏi UserRepository để khớp interface
- **Vấn đề:** `UserService` dùng tên method sai (không khớp `IUserService`: GetAllUsersAsync, GetUserByIdAsync, CreateUserAsync...)
- **Fix:** Đổi tên method trong UserService theo đúng interface
- **Vấn đề:** `DepartmentService` tên method không khớp `IDepartmentService`
- **Fix:** Đổi tên thành GetAllDepartmentsAsync, GetDepartmentTreeAsync, CreateDepartmentAsync...

### Lỗi cột database
- **Vấn đề:** SQL tạo cột `must_change_password` (lowercase) nhưng EF Core query `"MustChangePassword"` (PascalCase quoted)
- **Fix:** Đổi SQL thành `ALTER TABLE "AppUsers" ADD COLUMN "MustChangePassword" ...` (có ngoặc kép PascalCase)

### Frontend báo "phản hồi không hợp lệ" khi login
- **Vấn đề:** `ApiService.PostAsync` deserialize `LoginResponseDto` như `ApiResponse<LoginResponse>` (wrapper) nhưng backend trả trực tiếp
- **Fix:** Deserialize thẳng `ReadFromJsonAsync<LoginResponse>()`, bỏ wrapper class

### Tạo user trả 400
- **Vấn đề 1:** Frontend gửi `Role: "Clerk"` (string) nhưng backend nhận `RoleIds: List<Guid>` → role không được gán
- **Fix:** `AdminService.CreateUserAsync` gọi `GET /api/roles` trước để tìm GUID theo tên, rồi gửi đúng format
- **Vấn đề 2:** Form không validate `Username` và `Password` trước khi gửi → backend reject (empty string fail [Required])
- **Fix:** Thêm validation trong `Users.razor.SaveUser()` — check username, password >= 6 ký tự

### Toast bị che bởi modal
- **Vấn đề:** `modal-overlay` có `z-index: 1000`, Blazored.Toast mặc định thấp hơn → toast ẩn sau modal
- **Fix:** Thêm CSS `.blazored-toast-container { z-index: 10000 !important; }` vào app.css

---

## 5. 📦 Dependencies Đã Thêm

**Backend (NuGet):**
- `MailKit 4.8.0` — SMTP client
- `MimeKit 4.8.0` — Email builder

**Frontend (NuGet):**
- `Blazored.LocalStorage` — JWT storage
- `Blazored.Toast` — Notifications

---


---

## 7. 🔍 OCR Service (Backend — Python/FastAPI)

> Luu y: Day la phan duoc lam truoc khi su dung tro ly AI trong du an.
> Frontend cho OCR chua duoc lam — chi co backend.

### Cong nghe su dung:
- **FastAPI** — REST API framework (Python)
- **PaddleOCR** — nhan dien ky tu tieng Viet (hieu qua cao voi van ban hanh chinh)
- **pdf2image** — chuyen doi PDF sang anh (DPI 200) de OCR
- **MinIO** — object storage luu file PDF
- **Kafka** — message queue (tu dong kich hoat OCR khi co file moi)

### Files da lam:

**app/main.py**
- FastAPI app, CORS middleware
- Lifespan: khoi dong Kafka consumer khi service bat dau, dung khi tat

**app/api/routes.py — 3 endpoints:**
- POST /api/ocr/process — nhan { doc_id, minio_path, token }, chay OCR, tra ket qua
- POST /api/ocr/process-upload — upload PDF truc tiep, chay OCR (dung de test)
- GET /api/ocr/health — health check

**app/ocr/engine.py — OcrEngine class:**
- Wrapper PaddleOCR voi tieng Viet (lang="vi", use_angle_cls=True)
- extract_raw() — lay ket qua tho tu PaddleOCR
- extract_lines() — chuyen thanh list { text, confidence, box }

**app/ocr/extractor.py — Boc tach truong thong tin:**
- extract_doc_number() — Regex nhan dang so hieu: 123/QD-HAU, 456/CV-CNTT
- extract_issued_date() — Nhan dang ngay: DD/MM/YYYY hoac 
gay X thang Y nam Z
- extract_title() — Tim trich yeu sau cac tu khoa: V/v, Ve viec, Trich yeu, Kinh gui
- extract_issuing_org() — Lay co quan tu 10 dong dau trang (confidence > 0.7)
- extract_fields() — Goi tat ca, tra ve dict ket qua

**app/services/ocr_processor.py:**
- Ham chinh xu ly: tai PDF tu MinIO → chuyen anh → OCR → boc tach → goi DocumentService

**app/services/minio_service.py:**
- Ket noi MinIO, tai file PDF theo object name

**app/services/kafka_consumer.py:**
- Consumer chay trong background thread (daemon)
- Lang nghe topic document.uploaded
- Payload: { doc_id, minio_path, token }
- Co the bat/tat qua bien moi truong KAFKA_ENABLED

**app/services/document_service.py:**
- Goi HTTP den DocumentService de cap nhat ket qua OCR sau khi xu ly xong

### Trang thai:
- [x] Backend hoan chinh
- [ ] Frontend chua co (can bo sung trang xem/kiem tra ket qua OCR)
- [ ] Chua tich hop vao luong chinh cua ung dung (can DocumentService publish Kafka event)

## 6. 🗺️ Những Gì Chưa Làm / TODO

- [ ] JWT blacklist khi logout (hiện là stub)
- [ ] Refresh token lưu DB (hiện không persist)
- [ ] DocumentService: CRUD công văn đến/đi
- [ ] SignService: PKI ký số
- [ ] OCRService: nhận dạng văn bản
- [ ] Email verification khi thêm email mới
- [ ] 2FA (Two-Factor Authentication)
- [ ] Audit log (ai làm gì, khi nào)

