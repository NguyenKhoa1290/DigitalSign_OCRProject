# 📓 Nhật Ký Làm Việc với Dự Án HAU DigitalSign OCR

> Ghi lại theo thứ tự thời gian những gì đã làm, những vấn đề gặp phải và cách giải quyết.

---

## 🗓️ Buổi 1 — Khoảng 06/07/2026
### Khởi tạo dự án & xây dựng nền tảng

Bắt đầu từ một project .NET 9 trống. Nhiệm vụ đầu tiên là xây dựng toàn bộ IdentityService theo mô hình Clean Architecture.

**Làm gì:**
- Tạo solution `HAU_DigitalSign_OCR.slnx` với 5 project
- Thiết kế database schema cho hệ thống quản lý công văn đại học:
  - `AppUsers`, `AppRoles`, `AppUserRoles` (junction table), `Departments` (self-referencing tree)
- Xây dựng toàn bộ Core layer: 26 files (Entities, DTOs, Interfaces, Services interfaces, Common, Exceptions)
- Xây dựng Infrastructure layer: DbContext, 4 Repositories, 5 Services, Extensions
- Seed data ban đầu: 5 roles, 2 phòng ban (HAU root + Phòng Tổng hợp), 1 admin

**Quyết định thiết kế quan trọng:**
- Dùng `EnsureCreatedAsync()` thay vì EF Core Migrations vì môi trường dev đơn giản
- BCrypt work factor 12 cho hash password
- JWT HS256 với expire 60 phút
- Tên bảng/cột giữ nguyên PascalCase (PostgreSQL phân biệt hoa/thường khi có dấu `""`)

---

## 🗓️ Buổi 2 — Khoảng 11/07/2026
### Kafka Setup & Tích hợp Message Queue

**Làm gì:**
- Cài đặt và cấu hình Kafka cho hệ thống microservices
- Tạo `kafka_setup_guide.md` — hướng dẫn chi tiết cách chạy Kafka local
- Xác định topic `document.uploaded` để OCRService lắng nghe khi có file mới upload

**Vấn đề gặp:**
- Kafka cần Java, cấu hình phức tạp hơn so với Redis/RabbitMQ
- Quyết định cho phép tắt Kafka (`KAFKA_ENABLED=false`) để dev có thể chạy không cần Kafka

---

## 🗓️ Buổi 3 — Khoảng 13/07/2026
### Giao diện cây phòng ban & Fix UI

**Bối cảnh:** Backend đã trả về đúng dữ liệu nhưng Frontend chỉ hiển thị được cấp cha, không hiển thị cấp con.

**Vấn đề:**
- `GET /api/departments` trả flat list
- `GET /api/departments/tree` trả nested structure với `Children`
- Frontend `Departments.razor` chưa render đệ quy đúng cách

**Làm gì:**
- Fix `DepartmentService.GetDepartmentTreeAsync()` — build cây đệ quy từ flat list
- Cập nhật `Departments.razor` để hiển thị cây phân cấp (parent → children thụt lề)
- Hiển thị 2 dòng song song: tên phòng ban + thông tin cha

**Sự cố nghiêm trọng:**
- User thay `ParentId` của "Trường Đại học Kiến Trúc Hà Nội" sang "Phòng Tổng hợp"
- → Tạo ra vòng lặp vô hạn: HAU → Phòng TH → HAU → ...
- → API bị treo, database query infinite loop

**Giải pháp vòng lặp:**
- Thêm `HasCircularReferenceAsync()` trong `DepartmentService`
- Trước khi update ParentId, duyệt ngược cây từ parent mới lên root
- Nếu gặp lại chính node đang update → từ chối, trả lỗi 400

**Thảo luận bảo mật:**
- User hỏi: "Nếu hacker gửi API tạo vòng lặp thì sao?"
- Trả lời: Server-side validation là đủ vì chỉ Admin mới được update department, JWT được verify trước khi đến endpoint, và `HasCircularReferenceAsync()` ngăn chặn hoàn toàn dù gọi bao nhiêu lần

---

## 🗓️ Buổi 4 — 17/07/2026 (Buổi sáng)
### Lên kế hoạch luồng Onboarding & Khôi phục mật khẩu

**Yêu cầu nghiệp vụ từ user:**
- Admin tạo tài khoản → đặt mật khẩu tạm (mặc định là tên đăng nhập)
- User đăng nhập lần đầu → bắt buộc đổi mật khẩu ngay
- Lần đầu đổi mật khẩu → user có thể thêm email để khôi phục sau
- Quên mật khẩu → gửi OTP qua email Gmail → nhập OTP + mật khẩu mới
- SMTP: Gmail
- Email chỉ dùng để khôi phục mật khẩu, không dùng đăng nhập

**Làm gì:**
- Viết `onboarding_plan.md` — kế hoạch chi tiết 5 bước
- Thiết kế thêm entity `PasswordResetToken` và field `MustChangePassword` cho `AppUser`
- Thiết kế thêm DTOs: `ChangePasswordDto`, `ForgotPasswordDto`, `ResetPasswordDto`
- Thiết kế `IEmailService`, `IPasswordResetRepository` interfaces

---

## 🗓️ Buổi 4 — 17/07/2026 (Buổi chiều)
### Implement toàn bộ luồng Onboarding

**Backend:**
- Thêm `MustChangePassword` vào entity `AppUser`
- Tạo entity `PasswordResetToken` (lưu SHA-256 hash của OTP, không lưu plain text)
- Cài MailKit 4.8.0 + MimeKit 4.8.0 vào Infrastructure project
- Implement `EmailService.cs` — HTML email template đẹp gửi OTP qua Gmail SMTP
- Implement `PasswordResetRepository.cs` — tạo/lấy/vô hiệu hóa tokens
- Mở rộng `AuthService.cs` thêm 3 methods:
  - `ChangePasswordAsync` — đổi mật khẩu + tắt `MustChangePassword`
  - `ForgotPasswordAsync` — tạo OTP 6 số (crypto-secure), gửi email
  - `ResetPasswordAsync` — verify OTP hash, đặt mật khẩu mới
- Thêm 3 endpoints vào `AuthController`
- Update `appsettings.json` section EmailSettings
- Update `UserService.CreateUserAsync` — set `MustChangePassword = true`

**Frontend:**
- Thêm `MustChangePassword` vào `LoginResponse.cs`
- Tạo `PasswordModels.cs` — 3 request models
- Update `AuthService.LoginAsync` — trả tuple `(Success, MustChangePassword, Error)`
- Update `Login.razor` — redirect đến `/first-login` nếu `MustChangePassword = true`
- Tạo `FirstLogin.razor` — form đổi mật khẩu bắt buộc:
  - Thanh đo độ mạnh password
  - Ô nhập email/SĐT tùy chọn (để khôi phục sau)
- Tạo `ForgotPassword.razor` — nhập email, luôn trả "đã gửi" (chống user enumeration attack)
- Tạo `ResetPassword.razor` — nhập OTP 6 số (font to, dễ đọc) + mật khẩu mới

**Viết SQL migration thủ công** (vì dùng EnsureCreated, không dùng Migration):
```sql
ALTER TABLE "AppUsers" ADD COLUMN IF NOT EXISTS "MustChangePassword" BOOLEAN NOT NULL DEFAULT false;
CREATE TABLE IF NOT EXISTS "PasswordResetTokens" (...);
CREATE INDEX IF NOT EXISTS "IX_PasswordResetTokens_UserId_IsUsed" ...;
```
→ Lưu vào `sql_migration.md`

---

## 🗓️ Buổi 5 — 17/07/2026 (Tối)
### Sửa lỗi column database

**Lỗi:**
```
42703: column a.MustChangePassword does not exist
```

**Nguyên nhân:**
SQL tạo cột `must_change_password` (PostgreSQL lowercase khi không có dấu `""`)
nhưng EF Core generate query với `"MustChangePassword"` (PascalCase, có dấu `""`)
→ PostgreSQL phân biệt hoa/thường → không tìm thấy cột

**Fix:**
```sql
ALTER TABLE "AppUsers" RENAME COLUMN must_change_password TO "MustChangePassword";
-- Hoặc tạo đúng ngay từ đầu:
ALTER TABLE "AppUsers" ADD COLUMN "MustChangePassword" BOOLEAN NOT NULL DEFAULT false;
```
Cập nhật lại `sql_migration.md` với tên cột đúng.

---

## 🗓️ Buổi 6 — 18/07/2026 (Sáng)
### Sửa 3 lỗi liên tiếp

**Lỗi 1: Frontend báo "phản hồi không hợp lệ" khi login**

Nguyên nhân: `AuthController.Login` trả `LoginResponseDto` trực tiếp (`return Ok(dto)`)
nhưng `AuthService.cs` frontend đọc như `ApiResponse<LoginResponse>` (có wrapper `{ data: {...} }`)
→ `result.Data` luôn null

Fix: Đổi sang `ReadFromJsonAsync<LoginResponse>()` trực tiếp, bỏ wrapper class

---

**Lỗi 2: Tạo user mới luôn trả HTTP 400**

Nguyên nhân kép:
1. Frontend `CreateUserDto` gửi `Role: "Clerk"` (string tên)
   nhưng backend `CreateUserDto` nhận `RoleIds: List<Guid>`
   → Backend bỏ qua field `Role`, `RoleIds` = empty list
   → Model validation: password empty string fail `[Required]`

2. Form không validate `Username`, `Password` trước khi submit
   → Gửi empty string → backend reject 400

Fix:
- `AdminService.CreateUserAsync` gọi `GET /api/roles` trước → tìm GUID theo tên → gửi đúng `RoleIds`
- Thêm validation frontend: check username, password >= 6 ký tự trước khi gửi

---

**Lỗi 3: Toast thông báo hiển thị đằng sau modal**

Nguyên nhân: `modal-overlay` có `z-index: 1000`
Blazored.Toast container không set z-index → nằm dưới modal

Fix thêm vào `app.css`:
```css
.blazored-toast-container { z-index: 10000 !important; }
```

---

## 🗓️ Buổi 7 — 04/09/2026
### Viết tài liệu dự án

Tạo thư mục `tiendo/` với các file tài liệu:
- `1_kien_truc.md` — kiến trúc toàn bộ hệ thống
- `2_da_lam.md` — danh sách tính năng và fix đã làm
- `3_nhat_ky.md` — nhật ký này

Trong quá trình viết tài liệu phát hiện:
- OCRService đã có backend hoàn chỉnh (Python/FastAPI/PaddleOCR) nhưng chưa có frontend
- Cần bổ sung vào TODO: làm trang giao diện xem kết quả OCR

---

## 📊 Tổng Kết

| Thời điểm | Việc làm |
|---|---|
| 06/07/2026 | Khởi tạo, xây IdentityService Core + Infrastructure + API (26 files) |
| 11/07/2026 | Kafka setup, tích hợp message queue |
| 13/07/2026 | Fix cây phòng ban, chống circular reference |
| 17/07/2026 sáng | Lên kế hoạch onboarding & password recovery |
| 17/07/2026 chiều | Implement toàn bộ onboarding (backend + frontend) |
| 17/07/2026 tối | Fix lỗi column database (PascalCase vs lowercase) |
| 18/07/2026 | Fix login response, fix tạo user 400, fix toast z-index |
| 04/09/2026 | Viết tài liệu dự án (tiendo/) |

**Tổng số lỗi đã fix:** 6
**Tổng số tính năng đã làm:** 4 nhóm lớn
**Còn lại:** Frontend OCR, DocumentService, SignService, JWT blacklist, Refresh token persist

---

## 💡 Bài Học Rút Ra

1. **PostgreSQL + EF Core:** Tên cột phải dùng dấu `""` PascalCase đúng từ đầu khi tạo bảng thủ công — không để PostgreSQL tự convert lowercase

2. **ApiResponse wrapper:** Khi backend trả DTO trực tiếp (`return Ok(dto)`) thì frontend không được bọc thêm `ApiResponse<T>` khi deserialize

3. **Circular reference trong cây:** Luôn validate server-side trước khi cho phép update ParentId — không tin client

4. **z-index:** Modal và Toast cần được quản lý z-index rõ ràng. Quy ước: Modal = 1000, Toast = 10000

5. **Type mismatch giữa frontend DTO và backend DTO:** Frontend có thể dùng string (tên role) nhưng backend cần GUID — cần có lớp chuyển đổi ở service layer
