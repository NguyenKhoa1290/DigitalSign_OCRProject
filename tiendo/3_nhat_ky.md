# 📓 Nhật Ký Làm Việc với Dự Án HAU DigitalSign OCR

> Ghi lại theo thứ tự thời gian những gì đã làm, những vấn đề gặp phải và cách giải quyết.

> Tổng kết được đồng bộ ngày 05/10/2026. Các buổi/công việc cũ giữ thông tin theo thời điểm thực hiện; thông số hiện tại xem `1_kien_truc.md`, `2_da_lam.md`, `4_cau_truc_code.md` và `5_co_so_ha_tang_db.md`.

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
- Ban đầu cài MailKit 4.8.0 + MimeKit 4.8.0 vào Infrastructure project; sau đã nâng lên 4.18.0 ở Công việc số 3 để xử lý cảnh báo bảo mật.
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

## 🗓️ Cập nhật tài liệu — 14/09/2026
### Đồng bộ tài liệu với code thực tế

**Bối cảnh:** Các tài liệu cũ còn nhiều thông tin lệch với code hiện tại:
- Một số nơi ghi Gateway dùng Ocelot, trong khi code dùng YARP.
- Một số nơi ghi route `/api/v1/...`, trong khi controller hiện dùng `/api/...`.
- Workflow văn bản cũ ghi `Draft → PendingReview → Approved → Published`, trong khi code hiện dùng luồng ký nháy/ký BGH.
- TODO cũ vẫn ghi DocumentService, SignService, OCRService chưa làm, trong khi backend các service này đã có.

**Đã cập nhật:**
- `Claude.md` — viết lại tổng quan dự án theo code hiện tại.
- `tiendo/1_kien_truc.md` — cập nhật kiến trúc, port, route Gateway, workflow, service notes.
- `tiendo/2_da_lam.md` — cập nhật danh sách tính năng đã làm và TODO thực tế.
- `tiendo/4_cau_truc_code.md` — bổ sung bản đồ code cho Gateway, DocumentService, SignService, OCRService, Frontend.
- `tiendo/5_co_so_ha_tang_db.md` — cập nhật chiến lược DB, MinIO, Kafka, JWT.
- `IdentityService/README.md` và `OCRService/README.md` — sửa route/encoding/thông tin chạy theo code.

**Điểm tích hợp được ghi chú rõ:**
- `DocumentService` lưu file MinIO bằng tên GUID ngẫu nhiên và `MinioPath = documents/{storedFileName}`.
- `SignService` khi đó còn tìm PDF theo `{docId}.pdf`; vấn đề này đã được xử lý ở Công việc số 1 ngày 16/09/2026.
- Kafka event OCR khi đó gửi token rỗng; hạng mục service-token đã được xử lý ở Công việc số 5 ngày 17/09/2026.

---

## 🗓️ Đóng gói Docker — 16/09/2026
### Triển khai thử full stack bằng Docker Compose

**Đã làm:**
- Bổ sung Dockerfile cho ApiGateway, DocumentService, SignService, OCRService và Frontend.
- Mở rộng `docker-compose.yml` ở root để chạy full stack: PostgreSQL, MinIO, Kafka, IdentityService, DocumentService, SignService, OCRService, ApiGateway, Frontend.
- Chuyển PostgreSQL/MinIO/Kafka local về `localhost` cho cấu hình dev; trong Docker dùng hostname nội bộ `postgres`, `minio`, `kafka`.
- Đổi MinIO sang image `quay.io/minio/*` và Kafka sang `apache/kafka:3.7.2`.
- Thêm `TRIEN_KHAI_DOCKER.md` hướng dẫn mang source/image/data sang máy khác.
- Bổ sung README cho ApiGateway, DocumentService, SignService, Frontend.

**Đã kiểm tra:**
- `docker compose build` build thành công toàn bộ app image.
- `docker compose up -d` chạy được full stack.
- Health endpoint Gateway, Identity, OCR và Frontend trả 200.
- Login seed `admin / Admin@123` qua Gateway thành công.
- MinIO có bucket `documents`, Kafka có topic `document.uploaded`.

**Lưu ý còn lại:**
- Khi migrate dữ liệu thật cần backup thêm volume `hau_sign_certs`.
- OCR Kafka dùng service-token để tự PATCH kết quả về DocumentService; hạng mục này đã được xử lý ở Công việc số 5 ngày 17/09/2026.
- Frontend ký số khi đó cần đồng bộ DTO với backend SignService; vấn đề này đã được xử lý ở công việc tiếp theo ngày 16/09/2026.

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
| 14/09/2026 | Đồng bộ tài liệu với code thực tế |
| 16/09/2026 | Đóng gói Docker full stack và bổ sung hướng dẫn triển khai |
| 16/09/2026 | Chốt quy trình làm việc: code → build → Docker → test case → ghi test case → báo cáo |
| 16/09/2026 | Công việc số 1: sửa SignService đọc `Documents.MinioPath`, build/test/Docker/API ký nháy end-to-end pass |
| 16/09/2026 | Công việc số 2: sửa frontend ký số gửi đúng DTO backend, build/test/Docker/API payload frontend pass |
| 16/09/2026 | Công việc số 3: nâng `MailKit`/`MimeKit`, cài `wasm-tools`, bổ sung Python cho Docker frontend và build/test pass |
| 17/09/2026 | Công việc số 4: khôi phục Docker Desktop WSL2 sau khi mất image, build lại full stack, deploy và ghi hướng dẫn phục hồi |
| 17/09/2026 | Công việc số 5–6: bổ sung OCR service-token và màn hình xem kết quả OCR |
| 19/09/2026 | Công việc số 7: persist refresh token, rotate token, blacklist logout; build/test/Docker smoke test pass |
| 19/09/2026 | Công việc số 8: mở rộng ApiGateway kiểm tra blacklist qua IdentityService; build/test/Docker smoke test pass |
| 19/09/2026 | Công việc số 9: sửa Gateway fallback khi IdentityService validate-token tạm lỗi; build/test/Docker smoke test pass |
| 20/09/2026 | Công việc số 10: test full OCR upload PDF thật qua Kafka/PaddleOCR và bổ sung retry cho Kafka consumer; Docker/Gateway e2e pass |
| 20/09/2026 | Công việc số 11: đưa secret Docker Compose sang `.env`/`.env.example`, giữ default dev và smoke test Gateway/OCR/login pass |
| 20/09/2026 | Công việc số 12: kiểm thử ký số bằng role thật `Manager`/`BoardOfDirectors` qua Gateway; positive/negative role test pass |
| 20/09/2026 | Công việc số 13: kiểm thử UI ký số bằng Playwright trên frontend; Manager/Board ký và verify UI pass |
| 20/09/2026 | Công việc số 14: kiểm thử OCR với PDF dạng scan/image-based; Kafka/PaddleOCR bóc tách pass |
| 20/09/2026 | Công việc số 15: cấu hình Mailpit SMTP local và test forgot/reset password backend + frontend pass |
| 20/09/2026 | Công việc số 16: hoàn thiện `DeptSigned`, API `submit-director` và thao tác trình BGH trên UI |
| 24/09/2026 | Công việc số 17–18: Gmail gửi OTP thành công, người nhận xác nhận email; triển khai xác minh email first login |
| 27/09/2026 | Công việc số 19–29: theme HAU, CSS isolation, Dashboard, popup tài khoản, responsive, nút quay lại và chọn người nhận chứng thư |
| 28/09/2026 | Công việc số 30–36: CustomSelect, cache CSS, lớp modal/toast và breakpoint viewport/modal |
| 28/09/2026 | Công việc số 37–39: tự cấp chứng thư theo role, lọc người dùng theo đơn vị và bộ lọc tổ chức phụ thuộc |
| 28/09/2026 | Công việc số 40–42: bộ tài liệu vận hành, tài khoản mặc định và ba trang nền log/hoạt động/thông báo |
| 05/10/2026 | Công việc số 43: đồng bộ tài liệu với source và nhật ký, kiểm tra tính nhất quán tài liệu |
| 05/10/2026 | Công việc số 44: hoàn thiện sửa metadata, xem/tải PDF và phân công công văn; API/UI pass |
| 05/10/2026 | Công việc số 45: API thống kê thật và Dashboard theo role; SQL/API/UI desktop/mobile pass |

**Trạng thái hiện tại (đối chiếu source 05/10/2026):** IdentityService, DocumentService, SignService, OCRService, API Gateway và Frontend đều có code chính; Frontend có 17 page với CSS isolation. Bộ tài liệu có bốn nhóm vận hành. Tích hợp công văn và thống kê Dashboard đã build/triển khai Docker/API/UI trong Công việc số 44–45; xUnit gần nhất 65/65, trong đó Sign vẫn có 1 test rỗng.

**Còn lại đáng chú ý:** Backend log/audit/thông báo. Kiểm thử cần bổ sung: reset bằng OTP Gmail thật, bộ scan thật và coverage workflow/ký/OCR; Document hiện có 29 test nghiệp vụ. Ký PDF, workflow, reset qua Mailpit và OCR scan giả lập đã được kiểm thử Pass; gộp ký PDF với chuyển workflow là đề xuất cải tiến nếu nghiệp vụ yêu cầu. Chi tiết xem `2_da_lam.md`, mục 7.

---

## 🗓️ Công việc số 1 — 16/09/2026
### Sửa luồng SignService dùng đúng file MinIO của DocumentService

**Vấn đề:**
- DocumentService upload PDF vào bucket `documents` với tên GUID ngẫu nhiên và lưu DB dạng `MinioPath = documents/{storedFileName}`.
- SignService trước đó tự suy đoán object `{docId}.pdf`, nên không tìm được file thật khi chạy end-to-end.

**Đã sửa code:**
- Thêm `IDocumentFileRepository` trong SignService Core.
- Thêm `DocumentFileRecord` và mapping read-only sang bảng `Documents` bằng `ExcludeFromMigrations()`.
- Thêm `DocumentFileRepository` để đọc `MinioPath` theo `DocId`.
- Cập nhật `SignService` để normalize `documents/{storedFileName}` thành `{storedFileName}`, dùng cho cả ký và verify.
- Sửa `UsersController.GetCurrentUser()` đọc thêm `ClaimTypes.NameIdentifier`.
- Cập nhật integration test IdentityService theo route thực tế `/api/...` và response wrapper hiện tại.

**Đã kiểm tra theo quy trình:**
- `dotnet build .\HAU_DigitalSign_OCR.slnx`: pass. Cảnh báo `NU1902` cho `MailKit`/`MimeKit` đã được xử lý ở Công việc số 3.
- `dotnet test .\HAU_DigitalSign_OCR.slnx --no-build`: pass 29/29.
- `docker compose build identity-service sign-service`: pass.
- `docker compose up -d identity-service sign-service`: container chạy lại, `identity-service` healthy, `sign-service` up.
- API Docker/Gateway `TC-SIGN-001`: tạo văn bản, upload PDF, cấp certificate, ký nháy, verify chữ ký đều pass.

**Kết quả test API chính:**
- `DocId`: `68ca7361-149e-42b9-b8ec-da9b1f040a4e`
- `MinioPath`: `documents/c7e57808-de01-461a-8e0a-578c24d88bdb.pdf`
- `SignatureId`: `dc468d0b-5fc9-41c2-831e-51ac06e28529`
- Verify: `isValid = true`, `totalSignatures = 1`

**Bằng chứng log SignService:**
- Resolve path: `documents/c7e57808-de01-461a-8e0a-578c24d88bdb.pdf -> c7e57808-de01-461a-8e0a-578c24d88bdb.pdf`.

---

## 🗓️ Công việc số 2 — 16/09/2026
### Sửa frontend ký số gửi đúng DTO backend

**Vấn đề:**
- Frontend trước đó gọi SignService bằng payload `{ DocumentId, Comment }`.
- Backend `SignRequestDto` yêu cầu `DocId`, `SignerId`, có thể nhận thêm `SignerName`, `Reason`.
- `SignatureService.SignAsync()` chưa map đúng `DirectorSign` sang endpoint `legal-seal`.

**Đã sửa code:**
- Cập nhật `Frontend/Models/SignatureDto.cs` thêm `DocId`, `SignerId`, `SignatureTypeDisplay`, `SignResultDto`.
- Cập nhật `SignRequestDto` frontend có `DocId`, `SignerId`, `SignerName`, `Reason`.
- Cập nhật `Frontend/Services/SignatureService.cs`:
  - `DeptSign`/mặc định → `POST /api/signatures/personal-sign`.
  - `DirectorSign`/`LegalSeal` → `POST /api/signatures/legal-seal`.
  - Model verify khớp `VerifyResultDto` backend.
- Cập nhật `Frontend/Pages/Signatures/Index.razor` lấy `SignerId`/`SignerName` từ JWT qua `AuthService`.
- Cho phép role `Admin` vào màn ký số để khớp quyền backend và tiện test/dev.
- Cập nhật link từ trang chi tiết văn bản để `Admin` mở được màn chữ ký.

**Đã kiểm tra theo quy trình:**
- `dotnet build .\HAU_DigitalSign_OCR.slnx`: pass.
- `dotnet test .\HAU_DigitalSign_OCR.slnx --no-build`: pass 29/29.
- `docker compose build frontend`: pass.
- `docker compose up -d frontend`: pass.
- `Invoke-WebRequest http://localhost:5227`: HTTP 200.
- API Docker/Gateway `TC-FE-SIGN-002`: payload giống frontend mới ký nháy + ký pháp nhân + verify đều pass.

**Kết quả test API chính:**
- `DocId`: `53b45b16-6898-46a2-95af-f3c372e1744e`
- `MinioPath`: `documents/c8706004-aa77-4dcb-97b0-ca0c5b1d26cd.pdf`
- `PersonalSignatureId`: `2a572a2b-81ba-4a52-ba9e-008bb4b94561`
- `LegalSignatureId`: `49286c39-f890-4163-b53a-0e2945191ec9`
- Verify: `isValid = true`, `totalSignatures = 2`, `hasPersonalSignature = true`, `hasLegalSeal = true`.

---

## 🗓️ Công việc số 3 — 16/09/2026
### Nâng MailKit/MimeKit và cài wasm-tools cho Blazor WASM

**Vấn đề:**
- `MailKit` và `MimeKit` 4.8.0 bị cảnh báo bảo mật `NU1902`.
- Docker build frontend trước đó publish Blazor WASM không có `wasm-tools`, nên phải publish không tối ưu.
- Khi cài `wasm-tools` trong Docker, Emscripten cần `python` trong build image.

**Đã sửa code/cấu hình:**
- Nâng `MailKit` lên `4.18.0`.
- Nâng `MimeKit` lên `4.18.0`.
- Sửa `EmailService` để validate `EmailSettings:Username` và `EmailSettings:Password`, tránh truyền null vào MailKit/MimeKit API.
- Cài local workload `wasm-tools`.
- Cập nhật `Frontend/Dockerfile`:
  - Cài `python3` và symlink `/usr/bin/python`.
  - Cài `dotnet workload install wasm-tools` trong build stage.

**Đã kiểm tra theo quy trình:**
- `dotnet workload list`: đã có `wasm-tools`.
- `dotnet list IdentityService.Infrastructure.csproj package --vulnerable --include-transitive`: không còn package vulnerable.
- `dotnet build .\HAU_DigitalSign_OCR.slnx`: pass 0 warning/0 error.
- `dotnet test .\HAU_DigitalSign_OCR.slnx --no-build`: pass 29/29.
- `docker compose build identity-service frontend`: pass sau khi bổ sung `python3` cho frontend image.
- `docker compose up -d identity-service frontend`: pass.
- `GET http://localhost:5048/health`: HTTP 200.
- `GET http://localhost:5227`: HTTP 200.
- Login seed `admin / Admin@123` qua Gateway: pass.

**Ghi chú:**
- Lần đầu build frontend sau khi thêm `wasm-tools` bị lỗi `unable to find python in $PATH`; đã xử lý bằng cách cài `python3` trong Dockerfile.
- Cài `wasm-tools` local bằng .NET workload installer cũng cập nhật các workload đã có sẵn trên máy như Android/iOS/MAUI manifests/packs.
- Chưa test gửi email thật qua SMTP vì cần credential/app password hợp lệ và thao tác này có thể gửi email ra ngoài.

---

## 🗓️ Công việc số 4 — 17/09/2026
### Khôi phục Docker sau khi chuyển Hyper-V sang WSL2

**Bối cảnh:**
- Docker Desktop đang chạy bằng WSL2.
- Docker ban đầu không còn image/container (`Images: 0`).
- Cần build/deploy lại toàn bộ stack từ source.

**Đã thực hiện:**
- Build lại các image custom:
  - `hau/api-gateway:local`
  - `hau/identity-service:local`
  - `hau/document-service:local`
  - `hau/sign-service:local`
  - `hau/ocr-service:local`
  - `hau/frontend:local`
- Pull lại image nền khi `docker compose up -d`: PostgreSQL, MinIO, MinIO client, Kafka.
- Tạo file hướng dẫn nhanh `HUONG_DAN_KHOI_PHUC_DOCKER_WSL2.md`.
- Bổ sung link hướng dẫn phục hồi vào `TRIEN_KHAI_DOCKER.md`.

**Đã kiểm tra:**
- `docker compose ps`: các container chính đều `Up`; `postgres` và `identity-service` healthy.
- Gateway `/health`: HTTP 200.
- Gateway `/`: HTTP 200.
- Identity `/health`: HTTP 200.
- OCR `/api/ocr/health`: HTTP 200.
- Frontend `/`: HTTP 200.
- Login `admin / Admin@123`: pass, có access token, role `Admin`.
- MinIO có bucket `documents`.
- Kafka có topic `document.uploaded`.

**Ghi chú:**
- Lần đầu `docker compose build` bị kéo dài ở bước OCR `pip install`; đã dừng build tổng và build lại riêng `docker compose build ocr-service`, sau đó pass.
- Docker frontend publish lúc đó còn warning `Users._formDepartmentId` chưa được gán; warning không chặn build/deploy và đã được xử lý ở Công việc số 6.
- Vì Docker data mới, volume dữ liệu là môi trường mới rỗng; nếu cần dữ liệu thật phải restore backup PostgreSQL/MinIO/certs.

---

## 🗓️ Công việc số 5 — 17/09/2026
### Bổ sung service-token cho OCR Kafka cập nhật DocumentService

**Vấn đề:**
- DocumentService publish Kafka event `document.uploaded` sau upload file nhưng trường `token` đang rỗng.
- OCRService xử lý được PDF từ MinIO, nhưng trước đó chỉ PATCH kết quả về DocumentService khi có JWT người dùng.
- Luồng Kafka tự động cần cơ chế service-to-service để không phụ thuộc JWT dài hạn của người dùng.

**Đã sửa code/cấu hình:**
- `DocumentService.API` cho phép endpoint `PATCH /api/documents/{id}/ocr` nhận một trong hai cơ chế:
  - JWT người dùng như cũ.
  - Header nội bộ `X-Service-Token` cho OCRService.
- Thêm cấu hình `ServiceAuth:OcrServiceToken` và `ServiceAuth:OcrServiceUserId` cho DocumentService.
- `OCRService` fallback sang `SERVICE_TOKEN` khi Kafka event/request không có JWT.
- `OCRService` gửi `X-Service-Token` khi dùng service-token; nếu có JWT thì vẫn gửi `Authorization: Bearer ...`.
- Cập nhật `docker-compose.yml` để `document-service` và `ocr-service` dùng cùng token dev `hau-dev-ocr-service-token`.
- Cập nhật `.env.example`, README OCR và tài liệu triển khai.

**Đã kiểm tra theo quy trình:**
- `dotnet build .\HAU_DigitalSign_OCR.slnx`: pass; lúc đó còn warning cũ `Frontend/Pages/Admin/Users.razor(198,19)`, đã được xử lý ở Công việc số 6.
- `python -m compileall OCRService\app`: pass.
- `dotnet test .\HAU_DigitalSign_OCR.slnx --no-build`: pass 29/29.
- `docker compose build document-service ocr-service`: pass.
- `docker compose up -d document-service ocr-service api-gateway`: pass.
- OCR health `GET http://localhost:5051/api/ocr/health`: HTTP 200.
- Container OCR có `SERVICE_TOKEN`: pass.
- Test API service-token `TC-OCR-AUTH-005`: pass.

**Kết quả test API chính:**
- `DocId`: `d3ee8215-3759-4525-9b2a-8e7bc0c93d4d`.
- PATCH thiếu JWT/service-token: HTTP 401.
- PATCH bằng `X-Service-Token`: success.
- Gọi từ container OCR qua URL nội bộ `http://document-service:8080`: success.
- Giá trị xác nhận sau bước gọi container OCR:
  - `docNumber = OCR-SVC-005-CONTAINER`
  - `title = TC-OCR-SVC-005 OCR container service token`
- Sau khi rebuild/recreate lại `document-service` lần cuối, smoke test PATCH bằng service-token vẫn pass và request thiếu token vẫn trả HTTP 401.

**Ghi chú:**
- Token dev trong repo chỉ dùng local/demo. Khi triển khai thật cần đổi `ServiceAuth__OcrServiceToken` và `SERVICE_TOKEN` sang giá trị bí mật mới, đồng bộ giữa hai service.
- Chưa chạy full OCR bằng PaddleOCR qua Kafka upload thật trong hạng mục này; test tập trung vào cơ chế auth và đường PATCH nội bộ từ OCRService sang DocumentService.

---

## 🗓️ Công việc số 6 — 17/09/2026
### Thêm màn hình frontend xem kết quả OCR

**Vấn đề:**
- Backend DocumentService trả `DocNumber`, `DocTypeName`, `OcrDataRaw`, `Processes`, nhưng frontend model cũ đang đọc một số tên khác như `DocumentNumber`, `DocumentTypeName`, `OcrText`, `ProcessHistory`.
- Trang chi tiết công văn có khối “Nội dung OCR” nhưng chưa đọc đúng field OCR thực tế.
- Nút “Chạy OCR” trước đó chỉ trả success giả, chưa gọi OCRService.
- Build frontend còn warning cũ `_formDepartmentId` không được gán.

**Đã sửa code:**
- Cập nhật `Frontend/Models/DocumentDto.cs`:
  - Thêm field backend thật `DocNumber`, `DocTypeName`, `MinioPath`, `OcrDataRaw`, `Processes`.
  - Thêm alias tương thích `DocumentNumber`, `DocumentTypeName`, `FileUrl`, `OcrText`, `ProcessHistory`.
  - Thêm `MinioObjectName` để bỏ prefix bucket `documents/` trước khi gọi OCRService.
- Cập nhật `Frontend/Models/DocumentTypeDto.cs` để map `TypeName` từ backend và vẫn giữ alias `Name` cho UI cũ.
- Cập nhật `Frontend/Services/DocumentService.cs`:
  - `RunOcrAsync` gọi thật `POST api/ocr/process` qua Gateway.
  - Payload gồm `doc_id`, `minio_path`, `token=""`; OCRService sẽ fallback sang `SERVICE_TOKEN`.
- Thêm trang `Frontend/Pages/Documents/OcrResult.razor` tại route `/documents/{id}/ocr`.
- Cập nhật `Frontend/Pages/Documents/Detail.razor`:
  - Thêm nút “Kết quả OCR”.
  - Hiển thị tóm tắt OCR từ `OcrDataRaw`.
- Sửa warning frontend bằng cách bỏ field `_formDepartmentId` không được gán trong `Users.razor`.

**Đã kiểm tra theo quy trình:**
- `dotnet build .\HAU_DigitalSign_OCR.slnx`: pass 0 warning/0 error.
- `dotnet test .\HAU_DigitalSign_OCR.slnx --no-build`: pass 29/29.
- `docker compose build frontend`: pass.
- `docker compose up -d frontend`: pass.
- `GET http://localhost:5227`: HTTP 200.
- `docker compose ps frontend api-gateway document-service ocr-service`: các container liên quan đều `Up`.
- Test API/frontend route `TC-FE-OCR-006`: pass.

**Kết quả test chính:**
- `DocId`: `1273624e-2816-4bef-adf7-74fc636f2241`.
- PATCH OCR test bằng service-token: success.
- Gateway `GET /api/documents/{docId}` trả `docNumber = OCR-FE-006-20260917222721`.
- `ocrDataRaw` có dữ liệu JSON.
- Frontend route `GET http://localhost:5227/documents/{docId}/ocr`: HTTP 200.

**Ghi chú:**
- Test này kiểm tra route frontend và dữ liệu OCR mẫu đã có trong DocumentService.
- Chưa chạy full OCR PaddleOCR trên file PDF thật trong hạng mục này vì phần đó tốn thời gian/model và thuộc kiểm thử chất lượng OCR riêng.

---

## 🗓️ Công việc số 7 — 19/09/2026
### Persist refresh token và blacklist JWT khi logout

**Vấn đề:**
- Trước đó login trả refresh token nhưng refresh token chưa được lưu DB.
- `POST /api/auth/logout` mới trả thành công ở mức API, chưa revoke refresh token và chưa blacklist access token.
- Khi refresh token không được persist/rotate, không kiểm soát được reuse refresh token cũ sau khi refresh/logout.

**Đã sửa code:**
- Thêm entity `RefreshToken`:
  - Lưu `TokenHash` bằng SHA-256, không lưu plain text refresh token.
  - Gắn với `UserId`, `AccessTokenJti`, `ExpiresAt`, `RevokedAt`, `ReplacedByTokenHash`.
- Thêm entity `RevokedAccessToken`:
  - Lưu `Jti`, `UserId`, `ExpiresAt`, `RevokedAt` cho access token đã logout.
- Thêm repository:
  - `RefreshTokenRepository`
  - `RevokedAccessTokenRepository`
- Cập nhật `AuthService`:
  - Login lưu hash refresh token vào DB.
  - Refresh token kiểm tra DB, revoke token cũ và tạo token mới.
  - Reuse refresh token cũ sau khi rotate trả 401.
  - Logout revoke toàn bộ refresh token active của user.
  - Logout blacklist access token hiện tại nếu token còn hạn.
  - Validate-token trả invalid nếu token đã bị blacklist.
- Cập nhật `TokenService` thêm `GetPrincipalFromExpiredToken()` để refresh/logout đọc claim từ token đã hết hạn.
- Cập nhật `AuthController.Logout()` để lấy bearer token hiện tại và chuyển xuống service.
- Cập nhật `Program.cs`:
  - JWT bearer `OnTokenValidated` kiểm tra bảng `RevokedAccessTokens`.
  - Startup gọi `AuthStoreInitializer.EnsureAuthTablesAsync()` sau `EnsureCreatedAsync()`.
- Thêm `AuthStoreInitializer` để tạo bổ sung bảng `RefreshTokens` và `RevokedAccessTokens` trên PostgreSQL đã tồn tại vì IdentityService đang dùng `EnsureCreatedAsync()`.
- Bổ sung integration test cho:
  - Refresh token được persist và rotate.
  - Reuse refresh token cũ bị từ chối.
  - Logout làm protected endpoint IdentityService trả 401 với token cũ.
  - Logout làm `/api/auth/validate-token` trả `isValid=false`.
  - Refresh token sau logout bị từ chối.

**Đã kiểm tra theo quy trình:**
- `dotnet build .\HAU_DigitalSign_OCR.slnx`: pass 0 warning/0 error.
- `dotnet test .\IdentityService\tests\IdentityService.Tests\IdentityService.Tests.csproj`: pass 29/29.
- `dotnet test .\HAU_DigitalSign_OCR.slnx`: pass 31/31.
- Ban đầu Docker daemon chưa chạy; đã start Docker Desktop và xác nhận Docker server `29.5.3`.
- `docker compose build identity-service`: pass.
- `docker compose up -d identity-service api-gateway`: pass, `identity-service` healthy.
- Smoke test qua Gateway: login, refresh-token, reuse refresh cũ 401, logout, validate-token invalid, `/api/users` với token logout 401, refresh sau logout 401.
- Kiểm tra PostgreSQL: đã có bảng `RefreshTokens` và `RevokedAccessTokens`.

**Kết quả:**
- Phần code, automated test local, Docker build/redeploy và smoke test Gateway đã hoàn tất.
- Sau smoke test, DB có 2 dòng `RefreshTokens` và 1 dòng `RevokedAccessTokens` từ dữ liệu test.

**Ghi chú tích hợp:**
- Blacklist hiện có hiệu lực trong IdentityService và endpoint `/api/auth/validate-token`.
- Ở Công việc số 8, ApiGateway đã được mở rộng để gọi IdentityService `/api/auth/validate-token`, nên token đã logout bị chặn trước khi proxy sang Document/Sign/OCR.

---

## 🗓️ Công việc số 8 — 19/09/2026
### ApiGateway kiểm tra blacklist token qua IdentityService

**Vấn đề:**
- Công việc số 7 đã blacklist access token trong IdentityService.
- Tuy nhiên các route Document/Sign/OCR đi qua Gateway vẫn chỉ validate JWT local nếu Gateway không hỏi IdentityService, nên cần bổ sung bước kiểm tra blacklist tại Gateway.

**Đã sửa code/cấu hình:**
- Cập nhật `ApiGateway/Program.cs`:
  - Thêm `HttpClient` named client `identity-token-validation`.
  - Trong JWT `OnTokenValidated`, Gateway lấy bearer token hiện tại và gọi IdentityService `/api/auth/validate-token`.
  - Nếu IdentityService trả `isValid=false`, Gateway `Fail()` token và trả 401.
  - Ban đầu cấu hình fail closed khi IdentityService không sẵn sàng; việc này đã được sửa ở Công việc số 9 bằng cấu hình fail open có kiểm soát.
- Cập nhật `ApiGateway/appsettings.json`:
  - Thêm `AuthValidation:ValidateTokenUrl`.
  - Thêm `AuthValidation:TimeoutSeconds`.
- Cập nhật `docker-compose.yml`:
  - Thêm `AuthValidation__ValidateTokenUrl=http://identity-service:8080/api/auth/validate-token`.
  - Thêm `AuthValidation__TimeoutSeconds=3`.
- Cập nhật `ApiGateway/README.md` và tài liệu `tiendo`.

**Đã kiểm tra theo quy trình:**
- `dotnet build .\HAU_DigitalSign_OCR.slnx`: pass 0 warning/0 error.
- `dotnet test .\HAU_DigitalSign_OCR.slnx`: pass 31/31.
- `docker compose config --quiet`: pass sau khi sửa indent YAML.
- `docker compose build api-gateway`: pass.
- `docker compose up -d api-gateway`: pass.
- Gateway health `/health`: HTTP 200.
- Smoke test `TC-GW-AUTH-008`: pass.

**Kết quả test chính:**
- Login `admin / Admin@123`: OK.
- `GET /api/documents` trước logout với token hợp lệ: HTTP 200.
- Logout: `Đăng xuất thành công`.
- `POST /api/auth/validate-token` sau logout: `isValid=false`.
- `GET /api/documents` sau logout với cùng token: HTTP 401 từ Gateway.

**Ghi chú:**
- Gateway hiện phụ thuộc IdentityService cho bước blacklist validation trên các request có JWT.
- Sau Công việc số 9, nếu IdentityService không phản hồi trong `AuthValidation:TimeoutSeconds` và `AuthValidation:FailOpenOnValidationError=true`, Gateway fallback sang JWT local để tránh làm gián đoạn toàn bộ route downstream.

---

## 🗓️ Công việc số 9 — 19/09/2026
### Sửa Gateway fallback khi IdentityService validate-token tạm lỗi

**Vấn đề:**
- Sau Công việc số 8, Gateway gọi IdentityService để kiểm tra blacklist.
- Nếu IdentityService tạm dừng/timeout, Gateway fail closed và trả 401 cho mọi request có JWT, dù JWT vẫn hợp lệ local.
- Điều này làm Document/Sign/OCR bị gián đoạn theo IdentityService.

**Đã sửa code/cấu hình:**
- Cập nhật `ApiGateway/Program.cs`:
  - Thêm cấu hình `AuthValidation:FailOpenOnValidationError`.
  - Nếu IdentityService validate-token trả lỗi HTTP, response thiếu `isValid`, hoặc timeout/exception:
    - `FailOpenOnValidationError=true`: log warning và fallback sang JWT local.
    - `FailOpenOnValidationError=false`: giữ hành vi fail closed.
  - Nếu IdentityService phản hồi hợp lệ `isValid=false`, Gateway vẫn chặn 401 như trước.
- Cập nhật `ApiGateway/appsettings.json`:
  - `AuthValidation:FailOpenOnValidationError=true`.
- Cập nhật `docker-compose.yml`:
  - `AuthValidation__FailOpenOnValidationError=true`.
- Cập nhật tài liệu Gateway và `tiendo`.

**Đã kiểm tra theo quy trình:**
- `dotnet build .\HAU_DigitalSign_OCR.slnx`: pass 0 warning/0 error.
- `dotnet test .\HAU_DigitalSign_OCR.slnx`: pass 31/31.
- `docker compose config --quiet`: pass.
- `docker compose build api-gateway`: pass.
- `docker compose up -d api-gateway`: pass.
- Smoke test blacklist `TC-GW-AUTH-008`: vẫn pass.
- Smoke test fallback `TC-GW-AUTH-009`: pass.

**Kết quả test chính:**
- Khi IdentityService hoạt động:
  - Token đã logout vẫn bị Gateway chặn 401 trên `/api/documents`.
- Khi IdentityService tạm dừng:
  - Login lấy token trước khi dừng IdentityService.
  - `docker compose stop identity-service`.
  - `GET /api/documents` qua Gateway với JWT hợp lệ vẫn trả HTTP 200 nhờ fallback local.
  - `docker compose up -d identity-service` và health IdentityService trở lại HTTP 200.

**Ghi chú:**
- Đây là đánh đổi có chủ ý: khi IdentityService tạm lỗi, token đã logout có thể đi tiếp cho tới khi IdentityService phục hồi hoặc token hết hạn.
- Đổi `AuthValidation:FailOpenOnValidationError=false` nếu muốn ưu tiên bảo mật tuyệt đối hơn tính sẵn sàng.

---

## 🗓️ Công việc số 10 — 20/09/2026
### Kiểm thử full OCR upload PDF thật qua Kafka/PaddleOCR

**Vấn đề:**
- Các hạng mục trước đã test đường service-token và màn hình xem OCR, nhưng chưa chạy full luồng PaddleOCR trên PDF thật sau khi upload.
- OCRService có thể khởi động trước Kafka; trước khi sửa, consumer thread có rủi ro chết nếu Kafka chưa sẵn sàng.

**Đã sửa code/cấu hình:**
- Cập nhật `OCRService/app/services/kafka_consumer.py`:
  - Thêm retry loop khi Kafka chưa sẵn sàng hoặc consumer lỗi.
  - Đóng consumer trong `finally`.
  - Log exception khi xử lý từng message lỗi.
- Cập nhật `OCRService/app/main.py`:
  - Lấy event loop đang chạy trong FastAPI lifespan.
  - Kafka thread dùng `asyncio.run_coroutine_threadsafe(..., loop)` và `future.result()` để surface lỗi xử lý OCR.
- Cập nhật `OCRService/requirements.txt`:
  - Pin thêm `opencv-python==4.10.0.84` và `opencv-contrib-python==4.10.0.84` để Docker build không backtrack nhiều phiên bản OpenCV.

**Đã kiểm tra theo quy trình:**
- `python -m compileall .\OCRService\app`: pass.
- `docker compose build ocr-service`: pass.
- `docker compose up -d ocr-service`: pass.
- OCR health `GET http://localhost:5051/api/ocr/health`: HTTP 200.
- Kafka topic `document.uploaded`: tồn tại.
- Test Docker/Gateway `TC-OCR-E2E-010`: pass.

**Kết quả test chính:**
- Login `admin / Admin@123`: OK.
- Tạo document test qua Gateway: `bba87f60-5a39-43c6-801d-8adcf8fa478c`.
- Upload PDF test lên DocumentService:
  - File: `tc-ocr-e2e-010-20260920103916.pdf`.
  - MinIO path: `documents/63df61e8-04bf-47af-bdc4-069f3470d4a3.pdf`.
- DocumentService log publish Kafka OK:
  - topic `document.uploaded`, partition `0`, offset `0`.
- OCRService/PaddleOCR xử lý và PATCH kết quả về DocumentService trong khoảng 10 giây.
- Document sau OCR:
  - `docNumber = 123/CV-HAU`.
  - `issuedDate = 2026-09-20`.
  - `title = kiem thu OCR tu dong qua Kafka`.
  - Có `OcrDataRaw` chứa lines OCR và extracted fields.
  - Có `DocumentProcess` action `UpdateOCR` từ service user `00000000-0000-0000-0000-000000000051`.

**Ghi chú:**
- OCR nhận diện được nội dung PDF test ASCII lớn/chữ rõ. Chất lượng OCR với scan thật vẫn cần bộ test tài liệu thực tế riêng.
- Log custom của OCRService chưa hiện đầy đủ như log ASP.NET service, nhưng luồng thực tế đã được xác nhận bằng DB/API và log DocumentService.

---

## 🗓️ Công việc số 11 — 20/09/2026
### Đưa secret Docker Compose sang `.env`/`.env.example`

**Vấn đề:**
- `docker-compose.yml` đang hardcode các giá trị dev như PostgreSQL password, MinIO credential, JWT key và OCR service-token.
- Khi mang sang máy khác hoặc deploy thật, cần có cơ chế đổi secret rõ ràng mà không commit secret thật vào repo.

**Đã sửa code/cấu hình:**
- Thêm `.env.example` ở root project với các biến:
  - `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`.
  - `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD`.
  - `JWT_SECRET_KEY`, `JWT_ISSUER`, `JWT_AUDIENCE`, thời hạn token.
  - `OCR_SERVICE_TOKEN`.
- Cập nhật `docker-compose.yml`:
  - Dùng `${VAR:-default_dev}` cho các secret/cấu hình quan trọng.
  - Giữ default dev để local vẫn chạy nếu chưa tạo `.env`.
  - `minio-init` dùng cùng credential MinIO từ biến môi trường.
- Thêm `.gitignore` để bỏ qua `.env`/`.env.*` nhưng vẫn cho phép `.env.example`.
- Cập nhật `.dockerignore` để không đưa `.env` thật vào Docker build context.
- Cập nhật `OCRService/.env.example` để ghi rõ file này dùng khi chạy OCRService độc lập; full stack dùng `.env.example` ở root.
- Cập nhật `TRIEN_KHAI_DOCKER.md` với bước copy `.env.example` thành `.env` và checklist đổi secret.

**Đã kiểm tra theo quy trình:**
- `docker compose config --quiet`: pass với default dev.
- `docker compose --env-file .env.example config --quiet`: pass.
- `docker compose up -d`: pass, các container chính vẫn `Up`; `hau_postgres` và `hau_identity_service` healthy.
- Gateway health `GET http://localhost:5000/health`: `Healthy`.
- Identity health `GET http://localhost:5048/health`: `Healthy`.
- OCR health `GET http://localhost:5051/api/ocr/health`: `status=ok`.
- Login qua Gateway `admin / Admin@123`: pass, nhận access token.

**Ghi chú:**
- File `.env.example` dùng placeholder an toàn hơn cho deploy thật; nếu không tạo `.env`, Compose vẫn dùng default dev để tiện chạy local.
- Các file `appsettings*.json` vẫn giữ giá trị dev/local; Docker deploy nên override qua `.env`/environment variables.

---

## 🗓️ Công việc số 12 — 20/09/2026
### Kiểm thử ký số bằng role thật `Manager` và `BoardOfDirectors`

**Mục tiêu:**
- Xác nhận luồng ký số không chỉ pass với Admin/dev payload, mà pass với user có role nghiệp vụ thật.
- Kiểm tra authorization âm:
  - `BoardOfDirectors` không được gọi `personal-sign`.
  - `Manager` không được gọi `legal-seal`.

**Đã kiểm tra theo quy trình:**
- Tạo user test role `Manager`: `tc_manager_20260920131615`.
- Tạo user test role `BoardOfDirectors`: `tc_board_20260920131615`.
- Admin cấp certificate cho cả 2 user.
- Tạo document test, upload PDF lên MinIO qua Gateway.
- Submit document sang `PendingDeptReview`.
- Negative role test:
  - Board gọi `POST /api/signatures/personal-sign`: HTTP 403.
  - Manager gọi `POST /api/signatures/legal-seal`: HTTP 403.
- Positive role test:
  - Manager gọi `POST /api/signatures/personal-sign`: pass, tạo `PersonalSignature`.
  - Manager gọi `POST /api/documents/{docId}/dept-sign`: pass.
  - Board gọi `POST /api/signatures/legal-seal`: pass, tạo `LegalSeal`.
  - Board gọi `POST /api/documents/{docId}/director-sign`: pass.
  - Verify chữ ký: pass.

**Kết quả test chính:**
- Test case: `TC-SIGN-ROLE-012`.
- Document: `8a5d61d0-23bf-4e27-a1cb-8cf275c02e73`.
- MinIO path: `documents/a8330e0c-52ac-4a04-8f9a-6c20b5aff0cf.pdf`.
- Final document status: `DirectorSigned`.
- Signature count: `2`.
- Verify result: `isValid=true`, `signatureCount=2`.
- Process actions: `Submit, Submit, UpdateOCR, DeptSign, DirectorSign`.

**Ghi chú:**
- Test này chạy qua API Gateway bằng token của user role thật; chưa phải kiểm thử click UI bằng trình duyệt.
- Do upload PDF vẫn kích hoạt Kafka OCR, document có thêm action `UpdateOCR` trong lịch sử xử lý.

---

## 🗓️ Công việc số 13 — 20/09/2026
### Kiểm thử UI ký số trực tiếp trên frontend

**Mục tiêu:**
- Kiểm thử thao tác người dùng thật trên frontend, không chỉ gọi API.
- Xác nhận user role `Manager` và `BoardOfDirectors` có thể đăng nhập frontend, bấm ký số, bấm workflow và xác minh chữ ký trên UI.

**Cách chạy:**
- Tạo dữ liệu test bằng API Gateway:
  - User `Manager`.
  - User `BoardOfDirectors`.
  - Cấp certificate cho cả 2 user.
  - Tạo document, upload PDF, submit sang `PendingDeptReview`.
- Dùng Playwright headless thao tác frontend `http://localhost:5227`:
  - Login Manager.
  - Vào `/signatures/{docId}` và bấm `Ký nháy (Manager)`.
  - Vào `/documents/{docId}` và bấm workflow `Ký nháy`.
  - Login Board.
  - Vào `/signatures/{docId}` và bấm `Ký số pháp nhân (BGH)`.
  - Vào `/documents/{docId}` và bấm workflow `Ký số pháp nhân`.
  - Vào `/signatures/{docId}` và bấm `Xác minh chữ ký`.

**Kết quả test chính:**
- Test case: `TC-FE-SIGN-UI-013`.
- Document: `6fc9e8b8-3a55-4c77-932a-6c19d0b0f57d`.
- MinIO path: `documents/b4f6351b-5400-4dfa-925a-84752db670cd.pdf`.
- Manager user: `ui_manager_20260920062601`.
- Board user: `ui_board_20260920062601`.
- Final status: `DirectorSigned`.
- Signatures:
  - `PersonalSignature`.
  - `LegalSeal`.
- Verify API: `isValid=true`, `signatureCount=2`.
- Verify UI text: `✓ Tất cả 2 chữ ký đều hợp lệ`.
- Process actions: `Submit, Submit, UpdateOCR, DeptSign, DirectorSign`.

**Ghi chú:**
- Test này xác nhận thao tác click UI ký số chính đã pass.
- Vì upload PDF kích hoạt Kafka OCR, lịch sử document có thêm `UpdateOCR`; không ảnh hưởng luồng ký.

---

## 🗓️ Công việc số 14 — 20/09/2026
### Kiểm thử OCR với PDF dạng scan/image-based

**Mục tiêu:**
- Kiểm thử OCR với PDF không chứa text layer trực tiếp, mà chứa ảnh scan giả lập.
- Xác nhận luồng upload → Kafka → OCRService/PaddleOCR → PATCH kết quả OCR vẫn hoạt động với tài liệu kiểu scan.

**Cách chạy:**
- Tạo PDF test tạm ngoài repo bằng Python/Pillow:
  - Vẽ text lên ảnh A4 300 DPI.
  - Thêm nhiễu nhẹ, border scan và xoay nhẹ 0.7 độ.
  - Lưu ảnh thành PDF.
- Upload PDF qua Gateway vào DocumentService.
- Chờ Kafka event `document.uploaded`.
- Poll document cho tới khi `ocrDataRaw` có dữ liệu.

**Dữ liệu test:**
- Test case: `TC-OCR-SCAN-014`.
- Document: `dda7d33f-6c93-4e12-b42f-37c72da1fad5`.
- File test: `tc-ocr-scan-014-20260920133200.pdf`.
- MinIO path: `documents/8e958a57-0797-4397-8bbb-2d30e685a69c.pdf`.
- Nội dung ảnh PDF:
  - `TRUONG DAI HOC KIEN TRUC HA NOI`
  - `So: 456/QD-HAU`
  - `Ngay: 20/09/2026`
  - `V/v kiem thu OCR tu PDF scan`

**Kết quả test chính:**
- OCR hoàn tất sau poll đầu tiên khoảng 10 giây.
- `ocrDataRaw`: có dữ liệu.
- Số dòng OCR: `5`.
- Trường bóc tách:
  - `docNumber = 456/QD-HAU`.
  - `issuedDate = 2026-09-20`.
  - `title = kiem thu OCR tu PDF scan`.
- Process actions: `Submit, UpdateOCR`.

**Ghi chú:**
- Đây là PDF scan giả lập bằng ảnh, chưa phải tài liệu scan thật của nhà trường.
- Khi triển khai thực tế, vẫn nên bổ sung bộ scan thật với nhiều chất lượng ảnh khác nhau để đánh giá độ chính xác OCR.

---

## 🗓️ Công việc số 15 — 20/09/2026
### Cấu hình Mailpit và kiểm thử forgot/reset password

**Mục tiêu:**
- Test luồng forgot/reset password có gửi OTP thật qua SMTP nhưng không gửi email ra internet.
- Dùng Mailpit làm SMTP local/dev để bắt email OTP trong Docker.

**Đã sửa code/cấu hình:**
- Cập nhật `IdentityService.Infrastructure/Services/EmailService.cs`:
  - Thêm `EmailSettings:SecureSocketOptions`.
  - Thêm `EmailSettings:RequireAuth`.
  - Thêm `EmailSettings:FromEmail`.
  - Giữ default `StartTls` + auth cho SMTP thật/Gmail.
  - Cho phép SMTP local không auth, không TLS khi `RequireAuth=false`, `SecureSocketOptions=None`.
- Cập nhật `docker-compose.yml`:
  - Thêm service `mailpit` (`axllent/mailpit:latest`).
  - Expose SMTP `1025` và Web UI/API `8025`.
  - IdentityService dùng Mailpit mặc định trong Docker dev.
- Cập nhật `.env.example`, `TRIEN_KHAI_DOCKER.md`, `IdentityService/README.md` và tài liệu `tiendo`.

**Đã kiểm tra theo quy trình:**
- `dotnet build .\IdentityService\src\IdentityService.API\IdentityService.API.csproj`: pass 0 warning/0 error.
- `docker compose config --quiet`: pass.
- `docker compose build identity-service`: pass.
- `docker compose up -d mailpit identity-service api-gateway`: pass.
- Identity health: `Healthy`.
- Mailpit Web UI/API: HTTP 200.
- `dotnet test .\IdentityService\tests\IdentityService.Tests\IdentityService.Tests.csproj --no-build`: pass 29/29.

**Kết quả test backend/API:**
- Test case: `TC-AUTH-MAILPIT-015`.
- Tạo user `mailpit_user_20260920133942@hau.test`.
- Gọi `POST /api/auth/forgot-password`.
- Mailpit nhận email OTP từ `noreply@hau.local`.
- Lấy OTP 6 chữ số từ Mailpit.
- Gọi `POST /api/auth/reset-password`: thành công.
- Login bằng mật khẩu mới: thành công, `mustChangePassword=false`.

**Kết quả test frontend/UI:**
- Test case bổ sung: `TC-AUTH-MAILPIT-015-UI`.
- Tạo user `ui_reset_20260920064037`.
- Dùng Playwright thao tác frontend:
  - Vào `/forgot-password`.
  - Nhập email và gửi OTP.
  - Lấy OTP trong Mailpit API.
  - Vào `/reset-password`.
  - Nhập email, OTP, mật khẩu mới.
  - Reset thành công và redirect `/login`.
  - Login frontend bằng mật khẩu mới, vào được `/`.
- API login xác nhận lại:
  - `apiLoginUser = ui_reset_20260920064037`.
  - `mustChangePassword = false`.

**Ghi chú:**
- Mailpit chỉ dùng cho dev/test local, không gửi email ra ngoài.
- Khi deploy production, đổi biến `EMAIL_*` trong `.env` sang SMTP thật và test lại với credential thật.

---

## Công việc số 16 — 20/09/2026
### Hoàn thiện workflow `DeptSigned` và nút `submit-director`

**Mục tiêu:**
- Dùng `DeptSigned` làm trạng thái dừng thật sau khi lãnh đạo phòng ký nháy.
- Bổ sung bước trình Ban Giám hiệu ký trước khi `director-sign`.
- Sửa nút `Trình BGH ký` trên frontend để gọi được API thật.

**Đã sửa code:**
- `DocumentAction`: thêm `SubmitDirector`.
- `IDocumentService`: thêm `SubmitToDirectorAsync`.
- `DocumentService.DeptSignAsync`: chuyển `PendingDeptReview -> DeptSigned`.
- `DocumentService.SubmitToDirectorAsync`: chuyển `DeptSigned -> PendingDirectorSign`.
- `DocumentsController`: thêm `POST /api/documents/{id}/submit-director`.
- `Frontend/Services/DocumentService.cs`: thêm `SubmitDirectorDocumentAsync`.
- `Frontend/Pages/Documents/Detail.razor`: action `submit-director` gọi service mới.

**Đã kiểm tra theo quy trình:**
- `dotnet build .\DocumentService\src\DocumentService.API\DocumentService.API.csproj`: pass.
- `dotnet build .\Frontend\HauDocumentApp.csproj`: pass.
- `dotnet test .\DocumentService\tests\DocumentService.Tests\DocumentService.Tests.csproj`: pass 1/1.
- `docker compose config --quiet`: pass.
- `docker compose build document-service frontend`: pass.
- `docker compose up -d document-service frontend api-gateway`: pass.
- Gateway health: HTTP 200 `Healthy`.
- Frontend: HTTP 200.

**Test đã chạy:**
- `TC-DOC-WF-016`:
  - Document id: `7c186598-4e8d-4343-85a5-1f1a295743dc`.
  - Luồng: `Draft -> PendingDeptReview -> DeptSigned -> PendingDirectorSign -> DirectorSigned`.
  - Process actions: `Submit, Submit, DeptSign, SubmitDirector, DirectorSign`.
  - Kết quả: pass.
- `TC-DOC-WF-016-EDGE`:
  - Document id: `2808ff67-a5ab-4548-959d-e48fb01f0189`.
  - Gọi `director-sign` trực tiếp từ `DeptSigned`: HTTP 422.
  - Status giữ nguyên: `DeptSigned`.
  - Kết quả: pass.
- `TC-FE-WF-017`:
  - Manager user: `ui_wf_manager_20260920135343`.
  - Document id: `837da503-9e73-4085-a9fc-ce20a04c3c73`.
  - Dùng Playwright headless bấm nút `Trình BGH ký`.
  - Status sau UI action: `PendingDirectorSign`.
  - Kết quả: pass.

---

## Công việc số 17 — 24/09/2026
### Chuẩn hóa cấu hình gửi OTP bằng tài khoản Google

**Đã thực hiện:**
- Bổ sung đầy đủ `SecureSocketOptions`, `RequireAuth` và `FromEmail` trong cấu hình IdentityService.
- Bổ sung profile Gmail SMTP mẫu vào `.env.example`.
- Người gửi mặc định lấy từ `FromEmail`; nếu bỏ trống thì dùng `Username`.
- Ghi rõ quy trình dùng xác minh 2 bước và Google App Password trong tài liệu IdentityService và Docker.
- Không lưu Gmail/App Password thật trong repository.

**Kiểm tra:**
- `dotnet build .\IdentityService\src\IdentityService.API\IdentityService.API.csproj`: pass, 0 warning/0 error.
- `dotnet test .\IdentityService\tests\IdentityService.Tests\IdentityService.Tests.csproj --no-restore`: pass 29/29.
- `docker compose config --quiet`: pass.
- `docker compose build identity-service`: pass.
- `docker compose up -d identity-service api-gateway`: pass.
- Identity health và Gateway health: HTTP 200 `Healthy`.
- Đã cấu hình credential trong `.env` cục bộ được Git bỏ qua và recreate IdentityService/Gateway.
- Tạo user test `gmail_test_20260924221509` rồi gọi `POST /api/auth/forgot-password`.
- Gmail SMTP chấp nhận yêu cầu gửi, API trả HTTP 200; log IdentityService không có lỗi SMTP.
- Người nhận đã xác nhận email xuất hiện trong hộp thư.
- Chưa chạy bước dùng OTP của email này để reset password.

---

## Công việc số 18 — 24/09/2026
### Xác minh email trong lần đăng nhập đầu

**Đã thực hiện:**
- Thêm entity/bảng `EmailVerificationTokens` và repository riêng, không dùng chung OTP reset password.
- Thêm API có Bearer auth `POST /api/auth/send-email-verification`.
- Thêm email template OTP xác minh địa chỉ email.
- First login bắt buộc email và OTP; backend chỉ lưu email sau khi OTP hợp lệ.
- OTP ràng buộc theo user + email, lưu SHA-256 hash, hết hạn 15 phút và dùng một lần.
- Gửi lại mã sẽ vô hiệu hóa các mã xác minh cũ của user.
- `AppUsers.EmailVerifiedAt` lưu thời điểm xác minh; forgot password bỏ qua email chưa xác minh.
- Khi email bị thay đổi qua quản trị user, trạng thái xác minh được đặt lại.
- Frontend `/first-login` có nút gửi/gửi lại mã và ô nhập OTP 6 số.

**Đã kiểm tra theo quy trình:**
- Build IdentityService: pass, 0 warning/0 error.
- Build Frontend: pass, 0 warning/0 error.
- Unit test IdentityService: pass 34/34.
- `docker compose config --quiet`: pass.
- Build image `identity-service` và `frontend`: pass.
- Test tích hợp `TC-AUTH-EMAIL-VERIFY-019` qua Gateway + Mailpit: pass.
- Kiểm tra email chưa xác minh không nhận OTP forgot password; sau xác minh thì nhận được: pass.
- IdentityService, Gateway và Frontend sau khi trả về cấu hình Gmail: HTTP 200.
- API gửi template xác minh qua Gmail thật: HTTP 200.

---

## Công việc số 19 — 27/09/2026
### Làm lại giao diện theo nhận diện HAU và glassmorphism

**Đã thực hiện:**
- Thay icon tài liệu cũ bằng logo HAU nền trong suốt tại sidebar, các màn hình xác thực, màn hình loading và favicon.
- Sửa chân trang đăng nhập từ tên trường bị ghi nhầm thành `Trường Đại học Kiến trúc Hà Nội`.
- Áp dụng bảng màu tham chiếu từ cổng sinh viên HAU: xanh đậm `#12466D`, xanh `#3B5998`, xanh logo `#0093DD` và điểm nhấn xanh lá `#00963F`.
- Thiết kế ba cấp glassmorphism: sidebar kính tối, card kính sáng và control kính bên trong; modal có blur/đổ bóng nổi riêng.
- Bổ sung gradient nhiều lớp, orb chuyển động nhẹ, viền phát sáng, bóng màu và trạng thái hover/focus.
- Bổ sung `-webkit-backdrop-filter`, fallback nền đặc khi trình duyệt không hỗ trợ blur và `prefers-reduced-motion`.
- Responsive: sidebar thu gọn ở tablet; mobile dùng bottom navigation có cuộn riêng, không làm trang cuộn ngang.

**Đã kiểm tra theo quy trình:**
- `dotnet build .\Frontend\HauDocumentApp.csproj`: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- `docker compose build frontend`: pass, publish Release bằng `wasm-tools` thành công.
- `docker compose up -d frontend`: pass; container `hau_frontend` ở trạng thái `running`.
- HTTP frontend, CSS và logo: đều trả 200; logo trả `image/png`.
- Playwright Chromium kiểm tra login, first login, forgot password và dashboard ở desktop/mobile: pass.
- Test case: `TC-FE-GLASS-020`.

---

## Công việc số 20 — 27/09/2026
### Chuẩn hóa font, nền card và màu icon

**Đã thực hiện:**
- Khóa font giao diện và toàn bộ control form về `Inter`.
- Bỏ gradient khỏi card nội dung, stat card, table wrapper, modal và card xác thực; thay bằng nền kính một màu.
- Đồng bộ icon chức năng và thanh nhấn stat card về xanh HAU `#0093DD/#0879BD`.
- Giữ màu riêng cho cảnh báo, lỗi và trạng thái nghiệp vụ để không làm mất ngữ nghĩa.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass.
- Frontend Docker: HTTP 200, container `hau_frontend` đang `running`.
- Playwright Chromium kiểm tra dashboard desktop và login mobile: pass.
- Test case: `TC-FE-STYLE-021`.

---

## Công việc số 21 — 27/09/2026
### Tách CSS riêng cho từng page frontend

**Đã thực hiện:**
- Tạo đủ 13 file `.razor.css` tương ứng với 13 page Razor bằng cơ chế Blazor CSS isolation.
- Chuyển toàn bộ style tĩnh đang khai báo inline sang class có tên rõ nghĩa trong file CSS của page.
- Giữ `wwwroot/css/app.css` cho theme, design token và các component dùng chung.
- Loại bỏ màu inline riêng trên các stat card Dashboard để tiếp tục tuân thủ màu icon xanh HAU thống nhất.
- Chỉ giữ 3 inline style động: 2 giá trị biểu diễn độ mạnh mật khẩu và 1 giá trị cảnh báo hạn chứng thư.

**Đã kiểm tra theo quy trình:**
- Kiểm tra tự động: 13 page Razor, 13 file `.razor.css`, không thiếu cặp file.
- `dotnet build .\Frontend\HauDocumentApp.csproj`: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- `docker compose build frontend`: pass; publish Release bằng `wasm-tools` thành công.
- `docker compose up -d frontend`: pass; container `hau_frontend` đang `running`.
- Frontend và `HauDocumentApp.styles.css`: HTTP 200.
- Bundle CSS đã triển khai có đủ marker của 13 page.
- Playwright Chromium kiểm tra Dashboard, Admin Users, Admin Departments ở desktop và Login ở mobile: pass.
- Test case: `TC-FE-CSS-022`.

---

## Công việc số 22 — 27/09/2026
### Làm lại giao diện Dashboard theo mẫu HAU Docs

**Đã thực hiện:**
- Bọc nội dung trong `dashboard-page` để style của Dashboard được cô lập trong `Dashboard.razor.css`.
- Căn lại tiêu đề, lời chào, khoảng cách và chiều rộng nội dung theo ảnh mẫu.
- Thiết kế thẻ thống kê bo lớn, nền kính một màu, icon xanh HAU và giá trị thống kê rõ ràng.
- Thu nhỏ tiêu đề trang, lời chào, nhãn thống kê, giá trị và tiêu đề `Thao tác nhanh` để giao diện gọn hơn.
- Giữ nguyên các màu do người dùng cấu hình lại cho sidebar, menu đang chọn và avatar; không tự thay đổi bảng màu.
- Đồng bộ thông số thẻ thống kê `100px / 15px / 10px` cho desktop, tablet và mobile; đồng bộ bo góc sidebar `10px` trên mobile.
- Desktop hiển thị tối đa 4 thẻ một hàng; tablet 2 cột; mobile 1 cột.
- Thiết kế lại khối `Thao tác nhanh` toàn chiều rộng với header/body và button lớn hơn.
- Xử lý `GetStatsAsync()` trả `null` bằng `DashboardStats` mặc định để giao diện hiển thị `0` thay vì để trống.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass, publish Release bằng `wasm-tools` thành công.
- Frontend và scoped CSS: HTTP 200.
- Playwright Chromium: Dashboard Admin desktop 1440 × 900 và mobile 390 × 844 đều pass.
- Xác nhận 4 thẻ thống kê và khối thao tác nhanh hiển thị đúng ở cả hai viewport.
- Sau khi thu nhỏ typography: build/recreate Frontend Docker và kiểm tra HTTP lại đều pass.
- Sau khi đồng bộ kích thước: kiểm tra CSS triển khai giữ nguyên ba màu người dùng đã chỉnh và nhận đúng thông số responsive, pass.
- Test case: `TC-FE-DASHBOARD-023`.

---

## Công việc số 23 — 27/09/2026
### Sửa responsive navigation và bổ sung popup tài khoản

**Đã thực hiện:**
- Xóa chế độ sidebar thu gọn 88px ở khoảng 769–960px gây lỗi bố cục trên màn hình cỡ trung.
- Chuẩn hóa ba trạng thái: desktop trên 960px, mobile/bottom navigation từ 450px đến 960px, khóa ứng dụng dưới 450px.
- Cảnh báo màn hình quá hẹp được đặt ở cấp `App.razor`, áp dụng cả trang đăng nhập và các trang đã xác thực.
- Chuyển user card thành nút mở popup tài khoản; popup có `Chỉnh sửa thông tin` và `Đăng xuất`.
- Bổ sung tải hồ sơ bằng `GET /api/users/me` và lưu họ tên/email/số điện thoại bằng `PUT /api/users/{id}`.
- Giữ nguyên các màu sidebar/menu/avatar do người dùng đã cấu hình; chỉ sửa display, kích thước, vị trí và responsive.
- Dashboard chỉ còn một breakpoint responsive tại 960px.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass bằng `wasm-tools`.
- Playwright Chromium chạy 3 test: desktop 1440px, cửa sổ trung bình 900px và màn hình quá hẹp 340px; pass 3/3.
- Popup hiển thị đủ hai thao tác; modal tải đúng tài khoản Admin, lưu dữ liệu không đổi qua API và đăng xuất về `/login`: pass.
- Ở 900px sidebar dọc/compact không còn xuất hiện, bottom navigation được dùng: pass.
- Ở 340px nội dung ứng dụng bị ẩn và cảnh báo chiều rộng tối thiểu 450px hiển thị: pass.
- Test case: `TC-FE-ACCOUNT-RESPONSIVE-024`.

---

## Công việc số 24 — 27/09/2026
### Cho nội dung mobile cuộn phía sau taskbar

**Đã thực hiện:**
- Giữ nguyên vị trí taskbar mobile do người dùng cấu hình.
- Khai báo `--mobile-taskbar-height: 68px` và `--mobile-taskbar-bottom: 10px` để dùng thống nhất.
- Bỏ `padding-bottom` khỏi `.main-content`, cho vùng cuộn chiếm toàn bộ chiều cao và đi phía sau taskbar fixed.
- Đặt padding cuối `.page-container` bằng `28px + chiều cao taskbar + khoảng cách đáy`, hiện là `106px`.
- Đồng bộ vị trí popup tài khoản theo hai biến taskbar, tránh dùng số cố định.
- Không thay đổi màu giao diện.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass.
- Playwright Chromium tại 390 × 844: pass 1/1.
- Vùng cuộn chồng xuống phía sau taskbar 78px; padding cuối tính được 106px.
- Sau khi cuộn hết, khối nội dung cuối còn cách taskbar 37px và không bị che.
- Test case: `TC-FE-MOBILE-TASKBAR-025`.

---

## Công việc số 25 — 27/09/2026
### Nâng chiều rộng điện thoại tối thiểu lên 450px

**Đã thực hiện:**
- Đổi media query khóa ứng dụng từ `max-width: 359px` thành `max-width: 449px`.
- Cập nhật thông báo yêu cầu chiều rộng tối thiểu từ 360px lên 450px.
- Phạm vi mobile được chuẩn hóa thành 450–960px; desktop vẫn trên 960px.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass.
- Playwright Chromium kiểm tra hai giá trị biên: pass 2/2.
- 449px: ứng dụng bị khóa và hiện thông báo tối thiểu 450px.
- 450px: đăng nhập được, bottom navigation hiển thị đúng giao diện mobile.
- Test case: `TC-FE-VIEWPORT-LIMIT-026`.

---

## Công việc số 26 — 27/09/2026
### Bổ sung nút quay lại cho trang xem chữ ký

**Đã thực hiện:**
- Thêm nút `Quay lại` tại phần tiêu đề trang ký số.
- Khi đang xem `/signatures/{docId}`, nút quay về `/documents/{docId}` để giữ đúng ngữ cảnh công văn.
- Khi mở `/signatures`, nút quay về `/documents`.
- Trên giao diện mobile, nút chiếm toàn bộ chiều rộng để dễ thao tác; không thay đổi bảng màu hiện có.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass bằng `wasm-tools`.
- Playwright Chromium: pass 3/3 trường hợp desktop 1440px, mobile 450px và trang ký số chung.
- Nút hiển thị đúng và điều hướng về đúng chi tiết/danh sách công văn.
- Test case: `TC-FE-SIGN-BACK-027`.

---

## Công việc số 27 — 27/09/2026
### Đổi nền nút primary từ gradient sang màu cố định

**Đã thực hiện:**
- Thay nền gradient của `.btn-primary` bằng màu cố định `#304e8a`.
- Trạng thái hover tiếp tục dùng `#304e8a`, không phát sinh chuyển màu gradient.
- Giữ nguyên các gradient nền trang và hiệu ứng trang trí không thuộc nút.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass bằng `wasm-tools`.
- CSS triển khai: có 2 khai báo nền `#304e8a`, không còn gradient trong `.btn-primary`.
- Playwright Chromium desktop 1440px và mobile 450px: pass 2/2 cho trạng thái thường và hover.
- Test case: `TC-FE-BUTTON-COLOR-028`.

---

## Công việc số 28 — 27/09/2026
### Tìm và chọn người dùng khi cấp chứng thư số

**Đã thực hiện:**
- Thay ô bắt buộc nhập GUID bằng ô tìm kiếm hỗ trợ họ tên, username, email hoặc GUID.
- Kết nối trực tiếp `GET /api/users` với debounce 250ms và giới hạn 20 gợi ý.
- Mỗi gợi ý hiển thị họ tên, username và email; các người dùng có họ tên giống nhau vẫn xuất hiện thành các mục riêng.
- Sau khi chọn, giao diện hiển thị tài khoản đã chọn và request cấp chứng thư tự dùng GUID tương ứng.
- Không cho gửi yêu cầu nếu người dùng chỉ nhập nội dung nhưng chưa chọn một mục trong danh sách gợi ý.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass bằng `wasm-tools`.
- Playwright Chromium: pass 3/3 cho tìm tên trùng, tìm bằng GUID và viewport mobile 450px.
- Request cấp chứng thư gửi đúng GUID của người dùng thứ hai trong danh sách trùng tên.
- Test case: `TC-FE-CERT-USER-PICKER-029`.

---

## Công việc số 29 — 27/09/2026
### Đồng bộ giao diện ô chọn loại chứng thư

**Đã thực hiện:**
- Thay `<select>` loại chứng thư bằng dropdown tùy biến.
- Đồng bộ nền, viền, bo góc, bóng và trạng thái hover với danh sách gợi ý người dùng.
- Hiển thị dấu xác nhận tại loại chứng thư đang chọn và xoay biểu tượng mũi tên khi mở danh sách.
- Bổ sung `role=listbox`, `role=option`, `aria-expanded` và `aria-selected` đúng giá trị.
- Giữ nguyên hai giá trị nghiệp vụ `Personal` và `Organization`.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass bằng `wasm-tools`.
- Playwright Chromium desktop và mobile 450px: pass 2/2.
- Chọn `Pháp nhân (Organization)` cập nhật đúng giá trị và tự đóng danh sách.
- Test case: `TC-FE-CERT-TYPE-DROPDOWN-030`.

---

## Công việc số 30 — 28/09/2026
### Chuẩn hóa toàn bộ ô chọn frontend

**Đã thực hiện:**
- Tạo component dùng chung `Shared/CustomSelect.razor` và CSS isolation tương ứng.
- Tạo model `SelectOption` dùng thống nhất cho giá trị/nhãn lựa chọn.
- Thay 5 `<select>` còn lại: vai trò người dùng, phòng ban cha, loại văn bản, trạng thái công văn và loại công văn.
- Giữ callback tải lại danh sách khi đổi hai bộ lọc công văn.
- Hỗ trợ trạng thái đang chọn, dấu xác nhận, mũi tên mở/đóng, bàn phím và thuộc tính ARIA.
- Sửa `UserDto` đọc mảng `roles` của IdentityService để modal sửa người dùng hiển thị đúng vai trò hiện tại.

**Đã kiểm tra theo quy trình:**
- Toàn bộ `Frontend/Pages` và `Frontend/Shared` không còn thẻ `<select>`.
- Build Frontend: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass bằng `wasm-tools`.
- Playwright Chromium: pass 5/5 cho vai trò, dữ liệu động, bộ lọc công văn, mobile 450px và thao tác bàn phím.
- Test case: `TC-FE-CUSTOM-SELECT-031`.

---

## Công việc số 31 — 28/09/2026
### Sửa dropdown dài trong modal

**Đã thực hiện:**
- Giữ nguyên mẫu giao diện của dropdown loại chứng thư cho các ô chọn dùng chung.
- Giới hạn chiều cao danh sách vai trò và phòng ban cha; danh sách dài cuộn bên trong thay vì che phần chân modal.
- Nút `Hủy` và `Lưu thay đổi` luôn còn hiển thị khi dropdown vai trò đang mở.
- Giữ nguyên mapping `roles[]` để modal sửa người dùng hiển thị đúng vai trò hiện tại.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass bằng `wasm-tools`.
- Playwright Chromium: pass 1/1; chọn được mục cuối `Ban Giám hiệu` sau khi cuộn, danh sách tự đóng và không che footer.
- Test case: `TC-FE-CUSTOM-SELECT-032`.

---

## Công việc số 32 — 28/09/2026
### Ngăn trình duyệt dùng CSS cũ sau khi triển khai frontend

**Đã thực hiện:**
- Xác định dropdown bị rơi về nút HTML mặc định do trình duyệt giữ bản cũ của `HauDocumentApp.styles.css`, không phải do dữ liệu phòng ban.
- Thêm phiên bản truy vấn cho `app.css` và `HauDocumentApp.styles.css` trong `index.html` để buộc tải stylesheet mới.
- Cấu hình Nginx trả `Cache-Control: no-cache, no-store, must-revalidate` cho HTML và CSS.
- Không thay đổi màu sắc hoặc thông số giao diện của dropdown.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass bằng `wasm-tools`.
- Header cache của `index.html` và `HauDocumentApp.styles.css`: đúng `no-cache, no-store, must-revalidate`.
- Playwright Chromium: pass 1/1 cho dropdown phòng ban, vai trò và bộ lọc công văn; item hiển thị dạng flex toàn chiều rộng.
- Test case: `TC-FE-CUSTOM-SELECT-CACHE-033`.

---

## Công việc số 33 — 28/09/2026
### Cho dropdown vai trò hiển thị vượt khung modal

**Đã thực hiện:**
- Cho modal thêm/sửa người dùng hiển thị phần tử con vượt ra ngoài đường biên, tránh cắt danh sách vai trò tại đáy modal.
- Giữ dropdown mở xuống và nằm trên nội dung phía sau.
- Giữ chiều cao tối đa 160px và cuộn nội bộ cho danh sách dài.
- Không thay đổi màu sắc, kích thước control hoặc dữ liệu vai trò.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass bằng `wasm-tools`.
- Playwright Chromium: pass 1/1; danh sách vượt đáy modal, cao không quá 160px, cuộn được và chọn được `Ban Giám hiệu`.
- Test case: `TC-FE-ROLE-DROPDOWN-OVERLAY-034`.

---

## Công việc số 34 — 28/09/2026
### Chuẩn hóa popup responsive và kích thước viewport tối thiểu

**Đã thực hiện:**
- Sửa ngưỡng sử dụng ứng dụng thành chiều rộng tối thiểu 450px và chiều cao tối thiểu 720px.
- Nâng modal lên `z-index: 20000` và nâng stacking context chứa modal để luôn nằm trên sidebar, taskbar và dashboard.
- Giữ toast ở lớp cao hơn modal với `z-index: 30000`.
- Chuyển modal sang toàn màn hình khi viewport rộng không quá 960px hoặc cao không quá 800px.
- Thu gọn padding header/body/footer; phần body cuộn độc lập, header và footer luôn hiển thị.
- Áp dụng toàn màn hình cả cho popup chỉnh sửa tài khoản nằm trong navigation.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass bằng `wasm-tools`.
- Playwright Chromium: pass 7/7 tại biên 449/450px, 719/720px và các viewport 450×720, 1000×744, 1366×768, 1366×900.
- Popup người dùng, tài khoản và chứng thư đều nằm trên navigation; popup dài cuộn trong body.
- Test case: `TC-FE-RESPONSIVE-MODAL-035`.

---

## Công việc số 35 — 28/09/2026
### Hạ chiều cao viewport tối thiểu xuống 500px

**Đã thực hiện:**
- Hạ chiều cao tối thiểu để sử dụng ứng dụng từ 720px xuống 500px; giữ nguyên chiều rộng tối thiểu 450px.
- Cập nhật nội dung cảnh báo kích thước thiết bị và điều kiện media query tương ứng.
- Giữ chế độ popup toàn màn hình, body cuộn độc lập và header/footer luôn hiển thị trên màn hình thấp.
- Tăng phiên bản cache stylesheet để trình duyệt nhận ngay quy tắc responsive mới.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass bằng `wasm-tools`.
- Playwright Chromium: pass 4/4 tại các viewport 450×499, 450×500, 449×600 và 1366×500.
- Popup thêm người dùng tại 450×500 và 1366×500 hiển thị toàn màn hình, cuộn được, header/footer không bị che.
- Test case: `TC-FE-VIEWPORT-HEIGHT-036`.

---

## Công việc số 36 — 28/09/2026
### Giữ popup căn giữa trên desktop thông thường

**Đã thực hiện:**
- Chỉ dùng popup toàn màn hình trên mobile/máy tính nhỏ có chiều rộng không quá 1200px, hoặc viewport rất thấp có chiều cao không quá 650px.
- Desktop thông thường từ 1201px chiều rộng và trên 650px chiều cao tiếp tục dùng popup căn giữa.
- Đồng bộ điều kiện overflow của modal người dùng để dropdown không bị cắt trên desktop thông thường.
- Tăng phiên bản cache stylesheet để trình duyệt nhận ngay breakpoint mới.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass bằng `wasm-tools`.
- Playwright Chromium: pass 5/5.
- Mobile 450×500, desktop nhỏ 1200×800 và desktop rất thấp 1366×650 dùng toàn màn hình.
- Desktop mặc định 1366×768 và desktop lớn 1920×1080 giữ popup căn giữa.
- Test case: `TC-FE-MODAL-BREAKPOINT-037`.

---

## Công việc số 37 — 28/09/2026
### Cho người có quyền ký tự tạo chứng thư số

**Đã thực hiện:**
- Thêm API `POST /api/signatures/certificates/me/issue` cho `Manager` và `BoardOfDirectors`; danh tính người nhận lấy hoàn toàn từ JWT.
- Manager tự nhận chứng thư `Personal`, Ban Giám hiệu tự nhận chứng thư `Organization`; chặn cấp trùng nếu chứng thư hiện tại còn hiệu lực.
- Giữ API cấp cho người khác ở quyền `Admin`; Manager không thể truyền ID để cấp cho tài khoản khác.
- Thêm API xem chứng thư của tôi, Admin xem toàn bộ danh sách và Admin thu hồi chứng thư.
- Đồng bộ DTO cấp chứng thư frontend/backend: `UserId`, `Username`, `FullName`, `CertificateType`, `ValidityDays`.
- Bỏ trường mật khẩu khóa không được backend sử dụng; thời hạn ngày và loại chứng thư được xử lý thật trong X.509.
- Thêm trang `Chứng thư của tôi` và mục điều hướng cho Manager/Ban Giám hiệu; page có CSS isolation riêng.
- Trang Admin Certificates chuyển từ API stub sang danh sách/thu hồi thật.

**Đã kiểm tra theo quy trình:**
- Build toàn solution: pass, 0 warning/0 error.
- xUnit toàn solution: pass 36/36.
- `docker compose config --quiet`: pass.
- Build/recreate image `sign-service` và `frontend`: pass; hai container đang chạy.
- API qua Gateway: pass 7/7 cho tự cấp Manager/Ban Giám hiệu, chặn Clerk, chặn Manager cấp cho người khác, chặn cấp trùng và luồng Admin.
- Playwright Chromium: pass 2/2 cho trang tự cấp của Manager và modal cấp chứng thư Admin.
- Dữ liệu chứng thư test đã được thu hồi sau kiểm thử.
- Test case: `TC-SIGN-CERT-SELF-SERVICE-038`.

---

## Công việc số 38 — 28/09/2026
### Lọc người dùng theo Trường/Ban/Khoa khi Admin cấp chứng thư

**Đã thực hiện:**
- Thêm dropdown đơn vị theo cấu trúc cây vào trước ô tìm người dùng trong modal cấp chứng thư.
- Hiển thị tên, mã đơn vị và cấp phân nhánh để phân biệt Trường/Ban/Khoa.
- Khi đổi đơn vị, xóa lựa chọn và kết quả tìm kiếm cũ để tránh cấp nhầm người.
- Hiển thị tên đơn vị trong từng gợi ý người dùng.
- Mở rộng `GET /api/users` với query `departmentId`; kết hợp lọc đơn vị với họ tên, username, email hoặc GUID ngay tại PostgreSQL.
- Chọn đơn vị cha sẽ bao gồm chính đơn vị đó và toàn bộ đơn vị con; có lựa chọn `Tất cả đơn vị` để giữ khả năng tìm toàn hệ thống.

**Đã kiểm tra theo quy trình:**
- Build toàn solution: pass, 0 warning/0 error.
- xUnit toàn solution: pass 37/37; có test mới kiểm tra tập ID đơn vị cha/con.
- `docker compose config --quiet`: pass.
- Build/recreate image `identity-service` và `frontend`: pass.
- API Docker/Gateway: pass 3/3 cho lọc đơn vị con, đơn vị cha bao gồm con và kết hợp từ khóa; user test đã được xóa.
- Playwright Chromium: pass 2/2 trên desktop 1366×768 và mobile 450×500.
- Test case: `TC-FE-CERT-DEPARTMENT-FILTER-039`.

---

## Công việc số 39 — 28/09/2026
### Tách bộ lọc Trường, Ban/Khoa và đơn vị cấp dưới khi cấp chứng thư

**Đã thực hiện:**
- Thay dropdown cây chung bằng ba dropdown phụ thuộc: `Trường`, `Ban/Khoa/Phòng trực thuộc`, `Đơn vị cấp dưới`.
- Chỉ mở dropdown cấp sau khi đã chọn cấp cha và có dữ liệu con; đổi cấp cha sẽ xóa lựa chọn cấp dưới cùng kết quả tìm người dùng cũ.
- Tìm người dùng theo đơn vị cụ thể nhất đã chọn; lựa chọn `Tất cả...` tại từng cấp vẫn tìm trong đơn vị cha và toàn bộ đơn vị trực thuộc.
- Giữ danh sách dropdown giới hạn chiều cao và cuộn nội bộ; tăng phiên bản cache stylesheet.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- xUnit toàn solution: pass 37/37.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass; container đang chạy.
- Playwright Chromium: pass 2/2 trên desktop 1366×768 và mobile 450×500.
- Kiểm tra chuỗi phụ thuộc, lọc theo Phòng Tổng hợp, quay về phạm vi Trường và modal toàn màn hình mobile: pass.
- Test case: `TC-FE-CERT-ORG-CASCADE-040`.

---

## Công việc số 40 — 28/09/2026
### Tổ chức tài liệu thành ba thư mục chuyên trách

**Đã thực hiện:**
- Tạo `tai_lieu/01_test_cases` để quản lý danh mục test case, quy tắc và mẫu ghi test.
- Tạo `tai_lieu/02_trien_khai_tung_service` với hướng dẫn riêng cho hạ tầng và từng service Docker.
- Tạo `tai_lieu/03_nhat_ky_thay_doi` để lưu nhật ký tóm tắt và mẫu cập nhật.
- Giữ tài liệu `tiendo` làm lịch sử chi tiết, bổ sung liên kết hai chiều trong quy trình làm việc.

**Đã kiểm tra:**
- Kiểm tra 15 file Markdown trong `tai_lieu`: không có liên kết tương đối bị hỏng.
- `docker compose config --quiet`: pass.
- Đối chiếu danh sách Compose: đủ 11 service/hạ tầng và đúng tên dùng trong hướng dẫn.
- Không build lại ứng dụng vì thay đổi chỉ gồm tài liệu, không thay code hoặc cấu hình runtime.
- Test case: `TC-DOC-041`.

---

## Công việc số 41 — 28/09/2026
### Bổ sung tài khoản mặc định dùng cho kiểm thử

**Đã thực hiện:**
- Tạo `tai_lieu/04_tai_khoan_mac_dinh` và liên kết từ mục lục tài liệu.
- Ghi đúng tài khoản ứng dụng được seed trong code: `admin`, role `Admin`.
- Ghi tài khoản hạ tầng local mặc định của PostgreSQL, MinIO và Mailpit theo Docker Compose.
- Nêu rõ các role còn lại chưa có user mặc định và phải được Admin tạo để kiểm thử phân quyền.
- Cảnh báo không dùng các user timestamp của test cũ như tài khoản mặc định.

**Đã kiểm tra:**
- Login `admin` qua ApiGateway: pass, nhận đúng role `Admin`.
- Logout sau kiểm tra: pass.
- Kiểm tra liên kết Markdown và `docker compose config --quiet`: pass.
- Không build lại ứng dụng vì không thay code hoặc cấu hình runtime.
- Test case: `TC-AUTH-DEFAULT-042`.

---

## Công việc số 42 — 28/09/2026
### Mở rộng thao tác nhanh cho theo dõi hệ thống và thông báo

**Đã thực hiện:**
- Thêm nút `Nhật ký hệ thống` và `Hoạt động ứng dụng` cho Admin trong Dashboard.
- Thêm nút `Trung tâm thông báo` cho mọi tài khoản đã đăng nhập.
- Tạo route `/admin/system-logs`, `/admin/activity` có phân quyền Admin và `/notifications` có xác thực.
- Tạo giao diện nền mô tả nguồn log, nhóm audit event và loại thông báo dự kiến; ghi rõ dữ liệu thật chưa được kết nối.
- Mỗi page mới có file CSS isolation riêng, đồng bộ desktop/mobile và màu giao diện hiện tại.
- Tăng cache version stylesheet lên `20260928-11`.

**Đã kiểm tra theo quy trình:**
- Build Frontend: pass, 0 warning/0 error.
- xUnit toàn solution: pass 37/37.
- `docker compose config --quiet`: pass.
- Build/recreate image `frontend`: pass; container đang chạy.
- Playwright Chromium: pass 2/2 trên desktop 1366×768 và mobile 450×500.
- Điều hướng ba route, nội dung trạng thái và kiểm tra không tràn ngang mobile: pass.
- Test case: `TC-FE-DASHBOARD-QUICK-ACTIONS-043`.

---

## Công việc số 43 — 05/10/2026
### Đồng bộ tài liệu với source và nhật ký

**Đã thực hiện:**
- Đối chiếu `tai_lieu` và `tiendo` với code/cấu hình đang có; cập nhật mốc đối chiếu 05/10/2026.
- Sửa bộ tài liệu thành bốn nhóm, cập nhật 17 page frontend với đủ CSS isolation và danh mục 44 test case.
- Đồng bộ trạng thái Gmail theo Công việc số 17: người nhận đã xác nhận email, bước reset bằng OTP thật chưa chạy.
- Bổ sung trạng thái Dashboard chưa có API thống kê, ba trang nền chờ backend và các điểm tích hợp công văn còn lệch.
- Bổ sung `SubmitDirector`, phạm vi initializer DB, lớp modal/toast hiện tại và phân biệt ký PDF với chuyển workflow.
- Bổ sung các công việc còn thiếu trong bảng tổng kết; giữ kết quả test/viewport cũ theo thời điểm thực hiện.
- Ghi rõ Document/Sign test project chỉ có test rỗng; kết quả xUnit lịch sử không chứng minh coverage của hai service.
- Rà soát bổ sung theo phản hồi người dùng: mục 7 trong `2_da_lam.md` ghi riêng phần đã hoàn thành, phần source còn thiếu, kiểm thử cần bổ sung và đề xuất cải tiến. Giữ ký/workflow, OCR, reset qua Mailpit và gửi/nhận Gmail trong phần đã làm; gộp ký với workflow không được coi là yêu cầu còn thiếu đã chốt.

**Kiểm tra:**
- PowerShell kiểm tra 24 file Markdown: 26 liên kết tương đối, 0 liên kết hỏng.
- Sau rà soát bổ sung mục 7: kiểm tra lại 24 file, 28 liên kết tương đối hợp lệ; 13 mã test được dẫn trong mục 7 đều tồn tại, danh mục vẫn khớp 44 test case và diff whitespace pass.
- Đối chiếu `Frontend/Pages`: 17 page, 17 CSS isolation cùng tên.
- `docker compose config --quiet`: pass; `docker compose config --services`: đủ 11 thành phần.
- `git -c safe.directory=E:/DigitalSign_OCRProject diff --check`: pass.
- Test case: `TC-DOC-SYNC-044`; không tạo dữ liệu nghiệp vụ.
- Build ứng dụng, triển khai Docker và test runtime: không áp dụng, chỉ sửa tài liệu.
- Không sửa `.gitignore`; `tai_lieu/` đang bị bỏ qua, còn các file `tiendo` đã được Git theo dõi vẫn có diff.

---

## Công việc số 44 — 05/10/2026
### Hoàn thiện tích hợp công văn: sửa metadata, PDF và phân công

**Đã thực hiện:**
- Bổ sung `UpdateDocumentDto`, API PUT và lịch sử `Update`; Admin/Clerk/Specialist sửa ở Draft/Rejected, giữ nguyên file/OCR/trạng thái. Validate tiêu đề, loại văn bản và số hiệu.
- Bổ sung API file có JWT, tải đúng object theo `Documents.MinioPath`, PDF/no-store/range và attachment. Frontend tạo blob URL để xem/tải; thu hồi URL khi tải lại hoặc rời trang.
- Đồng bộ payload tạo/sửa (`DocNumber`, `DocTypeId`, DateOnly), phân công (`ToUserId`) và wrapper upload.
- Thêm danh bạ người nhận tối thiểu, modal tìm/chọn người nhận và kiểm tra tài khoản tồn tại/hoạt động trước khi phân công. Lỗi danh bạ trả 503, không ghi phân công; frontend giữ modal và hiện lỗi khi thao tác thất bại.
- Thêm cấu hình `IdentityService__BaseUrl` cho DocumentService trong Docker Compose; cache frontend `20261005-1`.
- Thay test rỗng DocumentService bằng 25 test nghiệp vụ; thêm script Node/Playwright `tests/document-edit-file-assignment.spec.cjs`. SignService test rỗng vẫn là việc cần bổ sung.
- Cập nhật kiến trúc, cấu trúc code, trạng thái công việc, triển khai và danh mục thành 47 test case. Không cần migration DB mới.

**Build, Docker và kiểm thử:**
- Build toàn solution: pass, 0 warning/0 error.
- xUnit: 61/61 pass (Identity 35, Document 25, Sign 1 test rỗng).
- Host thiếu runtime .NET 9: cài runtime 9.0.20 riêng trong `%TEMP%/hau-dotnet9`, không thay runtime toàn hệ thống; dùng `DOTNET_ROOT`/`DOTNET_ROOT_X64`/`VSTEST_DOTNET_PATH` khi chạy test.
- Build/recreate `identity-service`, `document-service`, `frontend`: pass; Identity healthy, Document/Frontend đang chạy. Build/recreate riêng Document lần cuối sau bổ sung kiểm tra JSON danh bạ null/array.
- Smoke API/UI: 13 nhóm pass trên Docker/Gateway, gồm đăng nhập UI, sửa/tạo công văn kèm upload, xem/tải PDF, phân quyền, người nhận không tồn tại/bị khóa, lỗi API phân công, modal desktop 1366×768 và mobile 450×500.
- PDF download kiểm tra byte-for-byte; HTTP range trả 206; chưa có file/không có văn bản trả 404; không có JWT trả 401.
- Ảnh modal được xem lại trong `tests/artifacts`; không lỗi JavaScript ở trang chi tiết.
- Dữ liệu lần chạy hoàn tất: xóa 2 document, 2 user và 2 object PDF tạm, logout các phiên test. Một lần chạy trước chạm rate limit 429; script đã hỗ trợ Retry-After, dữ liệu còn lại của lần đó đã được dọn bằng chế độ recovery.
- Test case: `TC-DOC-EDIT-045`, `TC-DOC-FILE-046`, `TC-DOC-ASSIGN-047`.
- Kiểm tra cuối: 24 file Markdown, 28 liên kết tương đối hợp lệ; danh mục khớp đủ 47 mã test; Compose config và diff whitespace của source/tài liệu pass. Các file build sinh tự động trong bin/obj không thuộc phạm vi diff kiểm tra source.

---

## Công việc số 45 — 05/10/2026
### Thống kê Dashboard từ dữ liệu thật

**Đã thực hiện:**
- Thêm `GET /api/documents/stats` có JWT, aggregate toàn database và actor theo JWT; không phụ thuộc phân trang. Ngày hôm nay UTC+7, người tạo/ngày tạo lấy process Submit sớm nhất theo luồng tạo hiện tại.
- Tách các chỉ số OCR, dự thảo/chờ phê duyệt của tôi, chờ ký nháy/chờ ký pháp nhân, số đã ký pháp nhân và distinct văn bản đã phân công. Phạm vi các chỉ số chung là toàn hệ thống như quyền đọc danh sách công văn hiện tại.
- Thêm `GET /api/users/stats` và `GET /api/signatures/certificates/stats` giới hạn Admin. Người dùng tính cả tài khoản khóa; chứng thư chỉ tính user certificate còn lưu và trong thời gian hiệu lực, không tính Root CA.
- Frontend gọi ba nguồn độc lập (Admin) hoặc chỉ Document (role khác). Giữ số liệu nguồn thành công, dùng nullable/`—` và cảnh báo khi nguồn lỗi/thiếu trường; thêm nút làm mới. Không dùng số 0 mặc định cho lỗi API.
- Thêm 4 test repository thống kê (tổng Document 29 test) và script SQL/API/Playwright `tests/dashboard-statistics.spec.cjs`.
- Không thay schema/migration, route Gateway hoặc scoped CSS.

**Build/triển khai/kiểm thử:**
- Build solution: 0 warning/0 error; xUnit 65/65 (Identity 35, Document 29, Sign 1 test rỗng).
- Build/recreate IdentityService, DocumentService, SignService và Frontend trên Docker local; backend/API chạy được. Rebuild/recreate Frontend lần cuối sau bổ sung phát hiện response thiếu trường dữ liệu.
- 11 nhóm smoke SQL/API/UI pass: API khớp truy vấn PostgreSQL độc lập cho Admin và 4 role còn lại, gồm 33 document khi có fixture (vượt trang 20); dữ liệu riêng theo đúng creator/actor; cấp/thu hồi chứng thư cập nhật đúng số đếm.
- Dashboard desktop 1366×768 và mobile 450×500; 5 role đúng chỉ số, role thường không gọi API quản trị, mô phỏng nguồn 503/response thiếu trường và làm mới phục hồi; không tràn ngang hoặc lỗi JavaScript. Ảnh desktop/mobile đã được xem lại.
- Test dùng 22 document fixture, 4 user và 1 chứng thư mới; thay status/history chỉ trên GUID fixture để kiểm tra thống kê, không kiểm thử ký PDF trong lần này. Xóa đúng document/process fixture trong PostgreSQL, xóa user, thu hồi chứng thư và logout các phiên test sau khi hoàn tất; không tạo object MinIO/email.
- Test case: `TC-DASHBOARD-STATS-048`, `TC-FE-DASHBOARD-STATS-049`; danh mục hiện 49 test case.
- Kiểm tra cuối: 24 file Markdown, 28 liên kết tương đối hợp lệ, 49 mã test chi tiết khớp danh mục; Compose config và diff whitespace source/tài liệu pass. Identity healthy, Document/Sign/Frontend đang chạy.

---

## Công việc số 46 — 05/10/2026
### Backend log/audit/thông báo và ba trang dữ liệu thật

**Đã thực hiện:**
- Thêm `ApiGateway/Monitoring`: native Npgsql 9.0.3, schema embedded resource, `EventStore`, middleware nhật ký và controller. GET `/api/admin/system-logs`, `/api/admin/activity` dành cho Admin, lọc service/level/from/to/trace/actor và phân trang; response no-store.
- Nhật ký HTTP bốn downstream service qua Gateway + startup Gateway. Audit auth/quản trị/ký/upload/xóa từ Gateway; trigger PostgreSQL trên `DocumentProcesses` ghi hành động công văn thật, kể cả OCR callback gọi thẳng DocumentService. Không thu gom toàn bộ Serilog/stdout nội bộ, không backfill lịch sử cũ.
- SQL initializer transaction tạo `MonitoringEvents`, `UserNotifications`, index/function/trigger, retry PostgreSQL 10 lần trước khi mở cổng. Compose thêm connection string PostgreSQL cho Gateway. Không tạo EF migration ở DocumentService.
- `DocumentProcessRepository` đặt trace bằng `set_config` trong transaction trước insert process; request Gateway trả `X-Trace-Id`, trace audit khớp thực tế qua YARP. Process/audit/thông báo là một transaction; metadata Document vẫn lưu riêng theo luồng hiện tại.
- Thông báo Assign cho người nhận; OCR/Reject/Signed cho người tạo; yêu cầu ký cho role Manager/Ban Giám hiệu đang hoạt động; Publish cho người tạo và người từng được phân công. API lấy user từ JWT; mark read/read-all chỉ user hiện tại, read idempotent. Alert chứng thư còn hiệu lực/hết hạn trong <=30 ngày tạo khi GET notifications, unique user+thumbprint.
- Frontend thay placeholder tại ba trang bằng API thật; `EventJournal` dùng chung hai trang Admin, đổi ngày Việt Nam sang UTC. Notifications có lọc, link chi tiết, unread count, đánh dấu đọc, polling 30 giây; hủy timer/request khi rời trang, xử lý đổi lọc trong lúc request còn chạy.
- Không lưu body/query/password/token/exception message trong bảng nhật ký. Path ngoài allowlist bị che; login username chỉ đọc trong bộ nhớ để xác định actor. Endpoint monitor/notification không tự log vòng lặp.

**Build/triển khai/kiểm thử:**
- Build solution 0 warning/0 error; xUnit 86/86: Gateway 21, Identity 35, Document 29, Sign 1 test rỗng. Runtime host 9.0.20 trong `%TEMP%/hau-dotnet9`.
- Docker build/recreate `api-gateway document-service frontend`; rebuild Gateway/Frontend sau sửa audit upload/thao tác thất bại và bộ lọc đổi trong lúc request đang tải.
- `tests/monitoring-notifications.spec.cjs`: 14 nhóm SQL/API/UI Pass trên image cuối. Kiểm tra 401/403, dữ liệu riêng/404 khi sửa thông báo người khác, pagination >20, read/read-all idempotent, loại thông báo workflow, chứng thư sắp hết hạn không lặp, năm nguồn log, bộ lọc và che dữ liệu nhạy cảm.
- Recreate riêng Gateway bằng Compose: audit/notification ID và ReadAt giữ nguyên, initializer không nhân bản lịch sử. UI đăng nhập Admin, trace/service/pagination/lỗi nguồn; thông báo tự render mới theo polling, đổi lọc giữa request tải đúng dữ liệu mới; điều hướng không lỗi JavaScript.
- Desktop 1366×768 và mobile 450×500 không tràn ngang; ảnh trong `tests/artifacts` đã được xem lại. Ký duyệt ở test là chuyển trạng thái metadata, không khẳng định có chữ ký PDF. OCR callback nội bộ được mô phỏng; PDF trắng riêng dùng kiểm tra audit upload, không đánh giá chất lượng OCR.
- Bổ sung whitelist route thực tế `/api/ocr/process-upload`, kiểm tra audit ProcessOCR/Warning khi thiếu file; alert chứng thư Admin dẫn tới trang quản trị, Manager/Board dẫn tới trang chứng thư cá nhân đúng quyền. Rebuild/recreate riêng Gateway và chạy lại 21 test Gateway cùng 14 nhóm smoke trên image cuối.
- Dọn đúng 3 document/process fixture (notices cascade), 5 user, 2 chứng thư, 1 object PDF, phiên test và event theo ID/trace test. Không gửi email, không xóa dữ liệu/log có sẵn.
- Test case mới: `TC-GW-LOGS-050`, `TC-AUDIT-051`, `TC-NOTIFICATIONS-052`; danh mục 52 test case. Việc còn lại: Gmail reset bằng OTP thật, bộ scan thực tế và coverage sâu ký/workflow/OCR; mở rộng thu gom log nội bộ/push nền chưa triển khai.
- Kiểm tra cuối: 24 file Markdown, 28 liên kết tương đối hợp lệ, 52 mã test chi tiết khớp danh mục, 17/17 page có CSS; Compose config và whitespace source/tài liệu Pass. Stack Docker đang chạy, Identity/PostgreSQL healthy; query xác nhận 0 user/document fixture monitoring còn lại.

---

## Công việc số 47 — 05/10/2026
### Ghi nhận bàn giao kiểm thử thực tế cho người dùng

- Theo yêu cầu người dùng: kiểm thử thực tế/nghiệm thu để người dùng tự thực hiện. Agent chỉ hỗ trợ khi được yêu cầu, không tự thử email/dữ liệu thực tế hoặc tự xác nhận Pass.
- Các mục Gmail OTP/reset thật, OCR với PDF/scan của nhà trường, công văn/ký PDF theo vai trò và đối chiếu Dashboard/log/audit/thông báo được ghi **Chờ người dùng kiểm thử và xác nhận** tại mục 7.3 của `2_da_lam.md`.
- Giữ kết quả kiểm thử tự động, Docker local và mô phỏng đã chạy theo phạm vi lịch sử; các kết quả này không thay cho nghiệm thu thực tế.
- Cập nhật quy trình và ghi chú test case tương ứng; chưa có kết quả nghiệm thu mới. Chỉ sửa tài liệu, không thay code, chạy test nghiệp vụ hoặc thêm mã test case; danh mục vẫn 52 test case.

---

## Công việc số 48 — 08/10/2026
### Đóng gói Cloudflare Tunnel bằng Docker

- Thêm `docker-compose.tunnel.yml` chạy `cloudflare/cloudflared` bằng `TUNNEL_TOKEN` lấy từ `.env.tunnel`; token không nằm trong source hoặc image.
- Frontend đọc `ApiBaseUrl` từ `appsettings.json`; container Nginx sinh cấu hình từ `PUBLIC_API_BASE_URL` khi khởi động. Có thể chuyển image sang máy khác và đổi API domain mà không build lại.
- Tunnel hiện có dùng `hauquanlycongvan.com → http://localhost:3000`; cloudflared dùng chung network namespace với Frontend. Nginx phục vụ giao diện và proxy `/api/**` đến Gateway; Gateway tiếp tục định tuyến Document/Sign/Identity/OCR theo path hiện có.
- Các port host trong Compose cơ sở bind vào `127.0.0.1`; cloudflared truy cập service qua mạng Docker, không cần mở cổng inbound trên VPS.
- Thêm `TRIEN_KHAI_CLOUDFLARE_TUNNEL.md`, biến mẫu trong `.env.example` và quy tắc LF cho script shell.
- Xác minh kỹ thuật: Compose merge/config hợp lệ; image Frontend build thành công; Frontend và connector đang chạy. Connector đăng ký 4 kết nối QUIC với Cloudflare. `https://hauquanlycongvan.com`, `/health` và `/appsettings.json` trả HTTP 200; một API yêu cầu đăng nhập trả HTTP 401 đúng kỳ vọng, xác nhận `/api/**` đã đi qua Nginx đến Gateway.
- Đây là kiểm tra hạ tầng và định tuyến. Kiểm thử nghiệp vụ/thực tế vẫn chờ người dùng thực hiện theo mục 7.3.
- API/R2 credentials từng xuất hiện trong ảnh không được ghi vào source hoặc `.env`; cần thu hồi và tạo lại. Chúng không thay thế Tunnel token.

---

## Công việc số 49 — 08/10/2026
### Hỗ trợ giao diện điện thoại rộng 300px

- Bỏ chặn giao diện ở viewport dưới 450px; ứng dụng hiện hỗ trợ từ 300px và chỉ hiển thị cảnh báo khi chiều rộng dưới 300px hoặc chiều cao dưới 500px.
- Bổ sung bố cục riêng cho khoảng 300–360px: giảm khoảng đệm, xếp dọc nút thao tác/modal/phân trang, co thanh điều hướng dưới, cho tab cuộn ngang và xử lý ngắt dòng nội dung dài.
- Thanh taskbar mobile chia đều các mục theo chiều rộng thực tế của màn hình; icon, chữ và avatar dùng kích thước co giãn, không còn tổng chiều rộng cố định làm avatar tràn khỏi khung ở tài khoản Admin.
- Trang quản lý người dùng thay hai nút Sửa/Xóa lặp lại bằng nút ba chấm và menu hành động trên desktop. Ở mobile, cột thao tác được ẩn; nhấn giữ hàng 550ms mở bảng Sửa/Xóa phía trên taskbar, còn thao tác cuộn sẽ hủy nhấn giữ.
- Trang Chứng thư số áp dụng cùng cơ chế: desktop dùng menu ba chấm, mobile ẩn cột thao tác và nhấn giữ chứng thư còn hiệu lực để mở hành động Thu hồi.
- Thử nghiệm UI danh sách dạng thẻ ở viewport <=768px cho Người dùng và Chứng thư số: bỏ cuộn ngang, ưu tiên tên/trạng thái và chia thông tin phụ thành lưới tự xuống cột ở 300–420px; desktop tiếp tục dùng bảng.
- Frontend lưu refresh token, tự làm mới access token trước khi hết hạn và thử lại một lần khi API trả 401; khóa đồng bộ ngăn nhiều request đồng thời xoay cùng refresh token. Đăng xuất gọi backend để thu hồi phiên rồi xóa cả hai token cục bộ.
- Thời hạn refresh token mặc định tăng từ 7 lên 180 ngày; access token vẫn 60 phút. Phiên cũ cần đăng nhập lại một lần để trình duyệt nhận refresh token và thời hạn mới.
- Tăng phiên bản URL CSS để trình duyệt không giữ stylesheet cũ. Build Frontend và IdentityService đều 0 warning/0 error; Compose merge/config hợp lệ; rebuild/recreate Frontend, IdentityService và cloudflared thành công.
- Kiểm tra kỹ thuật trên domain: `/health` trả HTTP 200 `Healthy`; IdentityService healthy và biến môi trường thực tế `JwtSettings__RefreshTokenExpiryDays=180`. HTML và stylesheet mới trả HTTP 200, có media query 300px và không còn luật chặn 449px; connector đăng ký đủ 4 kết nối QUIC.
- Chưa chạy nghiệm thu đăng nhập kéo dài bằng tài khoản thật. Mục này chờ người dùng đăng nhập lại một lần rồi xác nhận cơ chế tự làm mới sau khi access token hết hạn.
- Chưa đánh dấu nghiệm thu giao diện trên điện thoại thật; chờ người dùng kiểm tra và xác nhận theo yêu cầu bàn giao test thực tế.

---

## Công việc số 50 — 08/10/2026
### Sửa lớp hiển thị bộ lọc nhật ký và thông báo trên desktop

- Xác định menu `CustomSelect` bị cắt tại mép `.card` vì quy tắc dùng chung `overflow: hidden`; tăng `z-index` riêng cho menu không thể vượt qua vùng cắt này.
- Cho card bộ lọc của `EventJournal` và Trung tâm thông báo dùng `overflow: visible`, đồng thời tạo stacking context phía trên phần nội dung kế tiếp.
- Khi một `CustomSelect` mở, component gắn lớp `custom-select-open` và nâng lớp của chính container để danh sách lựa chọn hiển thị trên các control cùng hàng hoặc hàng sau.
- Sửa đồng thời ba trang: Hoạt động ứng dụng, Nhật ký hệ thống và Trung tâm thông báo. Tăng cache key scoped CSS lên `20261008-6`.
- Build Frontend 0 warning/0 error; rebuild/recreate Frontend và cloudflared thành công. Bản public trả stylesheet mới chứa đủ các quy tắc sửa lỗi; `/health` trả HTTP 200 `Healthy`.
- Kiểm tra trực quan bằng tài khoản thật trên máy tính vẫn chờ người dùng xác nhận theo quy ước nghiệm thu.

---

## 💡 Bài Học Rút Ra

1. **PostgreSQL + EF Core:** Tên cột phải dùng dấu `""` PascalCase đúng từ đầu khi tạo bảng thủ công — không để PostgreSQL tự convert lowercase

2. **ApiResponse wrapper:** Khi backend trả DTO trực tiếp (`return Ok(dto)`) thì frontend không được bọc thêm `ApiResponse<T>` khi deserialize

3. **Circular reference trong cây:** Luôn validate server-side trước khi cho phép update ParentId — không tin client

4. **z-index:** Modal và Toast cần được quản lý z-index rõ ràng. Quy ước hiện tại trong `Frontend/wwwroot/css/app.css`: Modal = 20000, Toast = 30000; cảnh báo viewport = 50000. Giá trị 1000/10000 trong buổi 6 là lịch sử trước khi chỉnh responsive.

5. **Type mismatch giữa frontend DTO và backend DTO:** Frontend có thể dùng string (tên role) nhưng backend cần GUID — cần có lớp chuyển đổi ở service layer
