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

**Trạng thái hiện tại:** IdentityService, DocumentService, SignService, OCRService backend, API Gateway và Frontend đều đã có code chính.

**Còn lại đáng chú ý:** JWT blacklist và refresh token persist, kiểm thử UI ký số thủ công trên trình duyệt với role thật.

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

## 💡 Bài Học Rút Ra

1. **PostgreSQL + EF Core:** Tên cột phải dùng dấu `""` PascalCase đúng từ đầu khi tạo bảng thủ công — không để PostgreSQL tự convert lowercase

2. **ApiResponse wrapper:** Khi backend trả DTO trực tiếp (`return Ok(dto)`) thì frontend không được bọc thêm `ApiResponse<T>` khi deserialize

3. **Circular reference trong cây:** Luôn validate server-side trước khi cho phép update ParentId — không tin client

4. **z-index:** Modal và Toast cần được quản lý z-index rõ ràng. Quy ước: Modal = 1000, Toast = 10000

5. **Type mismatch giữa frontend DTO và backend DTO:** Frontend có thể dùng string (tên role) nhưng backend cần GUID — cần có lớp chuyển đổi ở service layer
