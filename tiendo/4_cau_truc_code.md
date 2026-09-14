# 📦 Cấu Trúc Code — Hàm và Service Chi Tiết

> Tài liệu hóa đầy đủ các class, hàm, tham số, giá trị trả về trong từng service.
> File: `IdentityService` (backend C#) + `OCRService` (backend Python)

---

## 🗂️ I. IdentityService — Infrastructure Layer

### 1. AuthService.cs
**Namespace:** `IdentityService.Infrastructure.Services`
**Implements:** `IAuthService`
**Dependencies:** `IUserRepository`, `ITokenService`, `IPasswordResetRepository`, `IEmailService`

| Hàm | Tham số | Trả về | Mô tả |
|---|---|---|---|
| `LoginAsync` | `LoginRequestDto request` | `Task<LoginResponseDto>` | Xác thực username/password bằng BCrypt, cấp JWT + RefreshToken |
| `RefreshTokenAsync` | `RefreshTokenRequestDto request` | `Task<LoginResponseDto>` | Lấy principal từ token cũ (bỏ qua expiry), cấp cặp token mới |
| `ValidateTokenAsync` | `ValidateTokenRequestDto request` | `Task<ValidateTokenResponseDto>` | Kiểm tra chữ ký + hạn token, trả claims |
| `LogoutAsync` | `string userId` | `Task` | Stub — TODO: thêm JTI vào blacklist |
| `ChangePasswordAsync` | `Guid userId, ChangePasswordDto dto` | `Task` | Verify mật khẩu cũ, hash mật khẩu mới, tắt `MustChangePassword`, cập nhật email/SĐT nếu được cung cấp |
| `ForgotPasswordAsync` | `ForgotPasswordDto dto` | `Task` | Tạo OTP 6 số crypto-secure, lưu SHA-256 hash vào DB, gửi email. Không báo lỗi nếu email không tồn tại (chống user enumeration) |
| `ResetPasswordAsync` | `ResetPasswordDto dto` | `Task` | Verify OTP hash, đặt mật khẩu mới, vô hiệu hóa tất cả token OTP của user |

**Private helpers:**
```csharp
string GenerateOtp()
// RandomNumberGenerator.GetInt32(100000, 999999) — 6 chữ số, crypto-secure

string HashOtp(string otp)
// SHA256.HashData(UTF8(otp)) → hex lowercase — chỉ lưu hash, không lưu plain text
```

**Luồng LoginAsync:**
```
1. GetByUsernameAsync(username)   → throw InvalidCredentials nếu null
2. Kiểm tra IsActive              → throw AccountLocked nếu false
3. BCrypt.Verify(password, hash)  → throw InvalidCredentials nếu sai
4. Lấy danh sách roles từ UserRoles.Select(ur => ur.Role.RoleName)
5. GenerateAccessToken + GenerateRefreshToken
6. Trả LoginResponseDto (có MustChangePassword)
```

---

### 2. UserService.cs
**Namespace:** `IdentityService.Infrastructure.Services`
**Implements:** `IUserService`
**Dependencies:** `IUserRepository`, `IRoleRepository`, `IDepartmentRepository`

| Hàm | Tham số | Trả về | Mô tả |
|---|---|---|---|
| `GetAllUsersAsync` | `int page, int pageSize, string? search` | `Task<PagedResult<UserDto>>` | Phân trang + tìm kiếm theo username/fullname/email |
| `GetUserByIdAsync` | `Guid id` | `Task<UserDto>` | Lấy theo ID, throw `UserNotFoundException` nếu không có |
| `GetUserByUsernameAsync` | `string username` | `Task<UserDto>` | Lấy theo username |
| `CreateUserAsync` | `CreateUserDto dto` | `Task<UserDto>` | Validate uniqueness (username, email), validate dept + roles, hash BCrypt, set `MustChangePassword=true` |
| `UpdateUserAsync` | `Guid id, UpdateUserDto dto` | `Task<UserDto>` | Cập nhật FullName, Email, PhoneNumber, DepartmentId, IsActive |
| `DeleteUserAsync` | `Guid id` | `Task<bool>` | Xóa user |
| `AssignRoleAsync` | `Guid userId, Guid roleId` | `Task` | Kiểm tra user + role tồn tại, gán role |
| `RemoveRoleAsync` | `Guid userId, Guid roleId` | `Task` | Gỡ role |

**Private helper:**
```csharp
static UserDto MapToDto(AppUser user)
// Map entity → DTO, lấy DepartmentName từ navigation property
// Roles = UserRoles.Select(ur => ur.Role.RoleName)
```

**Validation trong CreateUserAsync:**
```
1. Check username chưa tồn tại
2. Check email chưa tồn tại (nếu có)
3. Check department tồn tại (nếu có DepartmentId)
4. Check từng roleId trong RoleIds tồn tại
5. BCrypt.HashPassword(password, workFactor: 12)
6. Tạo AppUser với MustChangePassword = true
```

---

### 3. DepartmentService.cs
**Namespace:** `IdentityService.Infrastructure.Services`
**Implements:** `IDepartmentService`
**Dependencies:** `IDepartmentRepository`

| Hàm | Tham số | Trả về | Mô tả |
|---|---|---|---|
| `GetAllDepartmentsAsync` | — | `Task<IEnumerable<DepartmentDto>>` | Flat list tất cả phòng ban |
| `GetDepartmentTreeAsync` | — | `Task<IEnumerable<DepartmentDto>>` | Cây phân cấp — lấy roots (ParentId=null), đệ quy gắn Children |
| `GetDepartmentByIdAsync` | `Guid id` | `Task<DepartmentDto>` | Lấy theo ID |
| `CreateDepartmentAsync` | `CreateDepartmentDto dto` | `Task<DepartmentDto>` | Validate parent tồn tại, tạo mới, DeptCode tự động ToUpper() |
| `UpdateDepartmentAsync` | `Guid id, CreateDepartmentDto dto` | `Task<DepartmentDto>` | Validate circular reference trước khi cập nhật ParentId |
| `DeleteDepartmentAsync` | `Guid id` | `Task<bool>` | Xóa phòng ban |
| `GetChildDepartmentsAsync` | `Guid? parentId` | `Task<IEnumerable<DepartmentDto>>` | Lấy danh sách con trực tiếp |

**Private methods:**
```csharp
async Task<bool> HasCircularReferenceAsync(Guid targetId, Guid proposedParentId)
// Duyệt ngược chuỗi tổ tiên của proposedParent
// Dùng HashSet<Guid> visited để tránh vô hạn khi DB đã corrupt
// Return true nếu gặp lại targetId → circular reference

static DepartmentDto MapToDto(Department d)
// Map phẳng, không có Children

static DepartmentDto MapToDtoWithChildren(Department d)
// Map đệ quy — gọi lại chính nó cho d.Children
```

**Thuật toán HasCircularReferenceAsync:**
```
Input: targetId = HAU(1), proposedParentId = PhongTH(2)
1. proposedParentId == targetId? → No
2. visited = {}; currentId = PhongTH(2)
3. Loop:
   - visited.Add(2) ✓
   - current = PhongTH → ParentId = HAU(1)
   - current.ParentId == targetId(1)? → YES → return true → throw 400
```

---

### 4. TokenService.cs
**Namespace:** `IdentityService.Infrastructure.Services`
**Implements:** `ITokenService`
**Dependencies:** `IConfiguration`

| Hàm | Tham số | Trả về | Mô tả |
|---|---|---|---|
| `GenerateAccessToken` | `AppUser user, IEnumerable<string> roles` | `string` | Tạo JWT HS256 với claims đầy đủ |
| `GenerateRefreshToken` | — | `string` | 32 bytes ngẫu nhiên → Base64 |
| `ValidateTokenAsync` | `string token` | `Task<bool>` | Kiểm tra chữ ký, issuer, audience, lifetime (ClockSkew=0) |
| `GetPrincipalFromExpiredToken` | `string token` | `ClaimsPrincipal?` | Validate chữ ký nhưng BỎ QUA expiry — dùng khi refresh token |

**JWT Claims trong GenerateAccessToken:**
```
sub         = user.Id (GUID)
jti         = Guid.NewGuid() (unique per token)
iat         = Unix timestamp
nameid      = user.Id
username    = user.Username  (custom claim)
name        = user.FullName
email       = user.Email (nếu có)
role        = mỗi role một claim riêng (nhiều role → nhiều claim)
```

---

### 5. EmailService.cs
**Namespace:** `IdentityService.Infrastructure.Services`
**Implements:** `IEmailService`
**Dependencies:** `IConfiguration`, MailKit, MimeKit

| Hàm | Tham số | Trả về | Mô tả |
|---|---|---|---|
| `SendPasswordResetEmailAsync` | `string toEmail, string toName, string otp` | `Task` | Gửi OTP qua Gmail SMTP (StartTLS port 587), HTML + plaintext fallback |

**Cấu hình đọc từ appsettings.json:**
```json
"EmailSettings": {
  "SmtpHost": "smtp.gmail.com",
  "SmtpPort": "587",
  "Username": "...",
  "Password": "...(App Password)...",
  "FromName": "HAU Documents"
}
```

---

## 🗂️ II. IdentityService — Repository Layer

### UserRepository.cs
**Implements:** `IUserRepository`
**Note:** Mọi query đều `Include(UserRoles.Role)` + `Include(Department)` để load navigation properties

| Hàm | SQL tương đương | Ghi chú |
|---|---|---|
| `GetByIdAsync(Guid id)` | `SELECT ... WHERE Id = @id` | Include roles + department |
| `GetByUsernameAsync(string)` | `SELECT ... WHERE Username = @u` | Case-sensitive |
| `GetByEmailAsync(string)` | `SELECT ... WHERE Email = @e` | |
| `GetAllAsync(page, pageSize, search?)` | `SELECT ... WHERE ... LIKE ... ORDER BY Username OFFSET ... LIMIT ...` | Tìm theo username/fullname/email |
| `CreateAsync(AppUser)` | `INSERT INTO AppUsers` | Sau khi insert, re-fetch để load navigation props |
| `UpdateAsync(AppUser)` | `UPDATE AppUsers` | |
| `DeleteAsync(Guid)` | `DELETE FROM AppUsers WHERE Id = @id` | |
| `AssignRoleAsync(Guid, Guid)` | `INSERT INTO AppUserRoles` | Idempotent — kiểm tra đã tồn tại trước |
| `RemoveRoleAsync(Guid, Guid)` | `DELETE FROM AppUserRoles` | |
| `GetUserRolesAsync(Guid)` | `SELECT RoleName FROM AppUserRoles JOIN AppRoles` | |

---

## 🗂️ III. Frontend — Services Layer (C# Blazor)

### AuthService.cs (Frontend)
**File:** `Frontend/Services/AuthService.cs`

| Hàm | Trả về | Mô tả |
|---|---|---|
| `LoginAsync(username, password)` | `(bool Success, bool MustChangePassword, string? Error)` | POST /api/auth/login, lưu token vào localStorage |
| `LogoutAsync()` | `Task` | Xóa token khỏi localStorage, notify auth state |
| `ChangePasswordAsync(dto)` | `(bool Success, string? Error)` | POST /api/auth/change-password (cần JWT) |
| `ForgotPasswordAsync(email)` | `(bool Success, string? Error)` | POST /api/auth/forgot-password |
| `ResetPasswordAsync(dto)` | `(bool Success, string? Error)` | POST /api/auth/reset-password |
| `IsAuthenticatedAsync()` | `Task<bool>` | Kiểm tra token còn hạn |
| `GetCurrentUserAsync()` | `Task<ClaimsPrincipal?>` | Parse JWT claims |
| `GetRoleAsync()` | `Task<string?>` | Lấy ClaimTypes.Role |
| `GetUserNameAsync()` | `Task<string?>` | Lấy ClaimTypes.Name |
| `GetUserIdAsync()` | `Task<string?>` | Lấy sub/nameid claim |

### AdminService.cs (Frontend)
**File:** `Frontend/Services/AdminService.cs`

| Hàm | Endpoint | Mô tả |
|---|---|---|
| `GetUsersAsync(page, pageSize, search?)` | GET /api/users | Phân trang |
| `GetUserAsync(Guid id)` | GET /api/users/{id} | |
| `CreateUserAsync(CreateUserDto)` | GET /api/roles → POST /api/users | Tự chuyển Role name → RoleId GUID |
| `UpdateUserAsync(Guid, UpdateUserDto)` | PUT /api/users/{id} | |
| `DeleteUserAsync(Guid)` | DELETE /api/users/{id} | |
| `GetDepartmentsAsync()` | GET /api/departments | Flat list |
| `GetDepartmentTreeAsync()` | GET /api/departments/tree | Cây phân cấp |
| `CreateDepartmentAsync(dto)` | POST /api/departments | |
| `UpdateDepartmentAsync(Guid, dto)` | PUT /api/departments/{id} | |
| `DeleteDepartmentAsync(Guid)` | DELETE /api/departments/{id} | |
| `GetRolesAsync()` | GET /api/roles | |

### ApiService.cs (Frontend)
**File:** `Frontend/Services/ApiService.cs`

| Hàm | Mô tả |
|---|---|
| `GetAsync<T>(url)` | GET request, SmartDeserialize kết quả |
| `PostAsync<T>(url, body?)` | POST request, SmartDeserialize kết quả |
| `PutAsync<T>(url, body?)` | PUT request |
| `PatchAsync<T>(url, body?)` | PATCH request |
| `DeleteAsync(url)` | DELETE request |
| `PostFormAsync(url, content)` | POST multipart/form-data |
| `SmartDeserialize<T>(json)` | Nếu JSON có field `"success"` → unwrap `.data`; ngược lại deserialize trực tiếp |

**Xử lý 401:** Tự động redirect về `/login`

---

## 🗂️ IV. OCRService — Python/FastAPI

### engine.py — OcrEngine

| Hàm | Tham số | Trả về | Mô tả |
|---|---|---|---|
| `__init__` | `language="vi", use_gpu=False` | — | Khởi tạo PaddleOCR với tiếng Việt, angle classifier |
| `extract_raw` | `image_path: str` | `list` | Kết quả thô PaddleOCR: `[[[box, (text, confidence)], ...]]` |
| `extract_lines` | `image_path: str` | `list[dict]` | Chuẩn hóa thành `[{text, confidence, box}]` |

### extractor.py — Bóc tách trường

| Hàm | Trả về | Pattern/Logic |
|---|---|---|
| `extract_doc_number(lines)` | `str?` | Regex: `\d{1,4}/[A-Z]{2,}-[A-Z]{2,}` — VD: `123/QD-HAU` |
| `extract_issued_date(lines)` | `date?` | Regex 2 dạng: `DD/MM/YYYY` hoặc `ngày X tháng Y năm Z` |
| `extract_title(lines)` | `str?` | Tìm sau từ khóa: `V/v`, `Về việc`, `Trích yếu`, `Kính gửi` |
| `extract_issuing_org(lines)` | `str?` | 10 dòng đầu, chứa từ khóa tổ chức, confidence > 0.7 |
| `extract_fields(lines)` | `dict` | Gọi tất cả 4 hàm trên, trả `{doc_number, issued_date, title, issuing_org}` |

### kafka_consumer.py

| Hàm | Mô tả |
|---|---|
| `start_consumer(process_fn)` | Khởi động background thread lắng nghe Kafka topic `document.uploaded` |
| `stop_consumer()` | Đặt `_running = False`, thread tự thoát |

**Kafka message payload:**
```json
{ "doc_id": "uuid", "minio_path": "file.pdf", "token": "JWT..." }
```

---

## 🗂️ V. Entities & DTOs quan trọng

### AppUser (Entity)
```csharp
Guid     Id
string   Username          // UNIQUE, max 50
string   PasswordHash      // BCrypt
string   FullName          // max 100
string?  Email             // UNIQUE
string?  PhoneNumber
Guid?    DepartmentId      // FK → Departments
bool     IsActive          // default true
bool     MustChangePassword // true khi admin tạo mới
DateTime CreatedAt

// Navigation
ICollection<AppUserRole> UserRoles
Department?              Department
```

### LoginResponseDto
```csharp
string       AccessToken
string       RefreshToken
Guid         UserId
string       Username
string       FullName
List<string> Roles
bool         MustChangePassword  // ← quan trọng: frontend check để redirect /first-login
```

### PagedResult<T>
```csharp
List<T> Items
int     TotalCount
int     Page
int     PageSize
int     TotalPages        // = ceil(TotalCount / PageSize)
bool    HasNextPage
bool    HasPreviousPage
```

### ApiResponse<T>
```csharp
bool         Success
string?      Message
T?           Data
List<string> Errors

// Factory methods:
static ApiResponse<T> Ok(T data, string message)
static ApiResponse<T> Fail(string message)
static ApiResponse<T> Fail(List<string> errors)
```
