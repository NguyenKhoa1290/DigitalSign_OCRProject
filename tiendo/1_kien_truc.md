# Kiến Trúc Dự Án - HAU DigitalSign OCR

> Đối chiếu với source ngày 05/10/2026; kết quả runtime lịch sử xem `3_nhat_ky.md` và `7_test_cases.md`.

## Tổng quan

```text
Frontend Blazor WebAssembly (:5227)
        |
        v
API Gateway (:5000) - YARP Reverse Proxy
        |
        +--> IdentityService (:5048)
        +--> DocumentService (:5049)
        +--> SignService (:5050)
        +--> OCRService (:5051)

Hạ tầng dùng chung:
- PostgreSQL: metadata nghiệp vụ
- MinIO: file PDF
- Kafka: event OCR tự động
```

## Cấu trúc thư mục

```text
DigitalSign_OCRProject\
├── ApiGateway\                      YARP gateway, JWT validation, rate limit
├── IdentityService\                 Auth, user, role, department
│   ├── src\IdentityService.API\
│   ├── src\IdentityService.Core\
│   └── src\IdentityService.Infrastructure\
├── DocumentService\                 Văn bản, upload, OCR result, workflow
│   ├── src\DocumentService.API\
│   ├── src\DocumentService.Core\
│   └── src\DocumentService.Infrastructure\
├── SignService\                     PKI nội bộ, ký PDF, verify chữ ký
│   ├── src\SignService.API\
│   ├── src\SignService.Core\
│   └── src\SignService.Infrastructure\
├── OCRService\                      Python FastAPI + PaddleOCR
└── Frontend\                        Blazor WebAssembly
```

## Service và port

| Service | Port | Vai trò |
|---|---:|---|
| Frontend | 5227 | UI Blazor, gọi API qua Gateway |
| ApiGateway | 5000 | Reverse proxy, JWT/blacklist, rate limit, API log/audit/thông báo PostgreSQL |
| IdentityService | 5048 | Đăng nhập, JWT, refresh token persist/rotate, logout blacklist, user, role, phòng ban, OTP reset |
| DocumentService | 5049 | Metadata văn bản, upload PDF, workflow, nhận kết quả OCR |
| SignService | 5050 | Cấp certificate, ký PDF, kiểm tra chữ ký |
| OCRService | 5051 | OCR PDF từ MinIO hoặc upload test |

## Route qua API Gateway

| Path | Service đích | Auth |
|---|---|---|
| `/api/auth/**` | IdentityService | Anonymous |
| `/api/users/**` | IdentityService | JWT |
| `/api/roles/**` | IdentityService | JWT |
| `/api/departments/**` | IdentityService | JWT |
| `/api/documents/**` | DocumentService | JWT |
| `/api/signatures/**` | SignService | JWT |
| `/api/ocr/**` | OCRService | JWT |
| `/api/admin/system-logs`, `/api/admin/activity` | ApiGateway | Admin |
| `/api/notifications`, `/{id}/read`, `/read-all` (dưới `/api/notifications`) | ApiGateway | JWT, chỉ dữ liệu của user hiện tại |

Gateway lưu nhật ký HTTP của bốn service qua proxy và sự kiện khởi tạo Gateway trong `MonitoringEvents`; không thu gom toàn bộ log nội bộ Serilog/stdout. Audit công văn phát sinh từ trigger PostgreSQL trên `DocumentProcesses`, gồm callback OCR gọi thẳng DocumentService; các thao tác auth/quản trị/ký, upload và xóa công văn được ghi tại Gateway. Không backfill lịch sử trước khi cài trigger.

`UserNotifications` nhận thông báo phân công, OCR, yêu cầu ký, từ chối, ký duyệt và phát hành. Yêu cầu ký gửi user hoạt động theo role Manager/Ban Giám hiệu, phù hợp phạm vi đọc công văn toàn hệ thống hiện tại. Chứng thư còn hiệu lực và sắp hết hạn trong 30 ngày được kiểm tra khi user mở/làm mới trang thông báo. Frontend polling 30 giây khi mở trang; chưa dùng push/SignalR. Log không lưu body/query/header chứa mật khẩu/token và che segment URL ngoài danh sách cho phép.

Gateway hiện dùng YARP, không dùng Ocelot. Route hiện là `/api/...`, không có prefix `/api/v1`.

## IdentityService

Kiến trúc Clean Architecture:

- `Core`: entity, DTO, interface, exception.
- `Infrastructure`: EF Core, repository, service, JWT, BCrypt, email.
- `API`: controller, middleware, Swagger, auth pipeline.

Chức năng chính:

- Login, logout, refresh token, validate token.
- Refresh token lưu DB dạng SHA-256 hash, được rotate sau mỗi lần refresh.
- Logout revoke refresh token active và blacklist access token theo `jti`.
- Validate token kiểm tra thêm blacklist trong `RevokedAccessTokens`.
- Đổi mật khẩu, bắt đổi mật khẩu lần đầu bằng `MustChangePassword`.
- Quên mật khẩu qua OTP email, OTP lưu SHA-256 hash.
- First login bắt buộc xác minh email bằng OTP trước khi lưu email và đổi mật khẩu; forgot password chỉ gửi mã cho email đã xác minh.
- CRUD user, gán/gỡ role.
- Danh bạ tối thiểu `GET /api/users/assignees?search=...` cho Admin/Clerk/Manager/BoardOfDirectors chọn người nhận công văn; chỉ trả ID, username, họ tên và tên đơn vị của tài khoản hoạt động.
- CRUD department và cây phòng ban, có chống vòng lặp parent-child.

ApiGateway validate JWT cục bộ bằng signing key trước, sau đó gọi IdentityService `/api/auth/validate-token` để chặn token đã logout theo blacklist.

Roles seed:

| Role | Ý nghĩa |
|---|---|
| `Admin` | Quản trị hệ thống |
| `Clerk` | Văn thư |
| `Specialist` | Chuyên viên |
| `Manager` | Lãnh đạo phòng |
| `BoardOfDirectors` | Ban Giám hiệu |

## DocumentService

Chức năng chính:

- Tạo/xem/xóa văn bản.
- Lấy danh sách loại văn bản.
- Upload PDF lên MinIO.
- Nhận kết quả OCR qua `PATCH /api/documents/{id}/ocr`.
- Endpoint OCR chấp nhận JWT người dùng hoặc header nội bộ `X-Service-Token` khi OCRService gọi service-to-service.
- Ghi log xử lý bằng `DocumentProcess`.
- Chạy workflow văn bản.

Workflow theo code:

```text
Draft
  -> PendingDeptReview
  -> DeptSigned
  -> PendingDirectorSign
  -> DirectorSigned
  -> Published
```

Luồng hiện tại dùng `DeptSigned` làm trạng thái dừng riêng sau khi lãnh đạo phòng ký nháy. Manager cần gọi tiếp `SubmitToDirectorAsync` / `POST /api/documents/{id}/submit-director` để chuyển văn bản sang `PendingDirectorSign`.

Ký PDF trong SignService và chuyển trạng thái trong DocumentService là hai thao tác riêng. Trang `/signatures/{docId}` gọi API ký PDF; trang `/documents/{docId}` gọi API workflow. Các endpoint workflow ký hiện không tự gọi SignService hoặc kiểm tra chữ ký PDF đã được tạo.

Từ chối:

```text
PendingDeptReview / DeptSigned / PendingDirectorSign -> Rejected
```

Các action log:

```text
Submit, DeptSign, SubmitDirector, DirectorSign, Reject, Publish, Assign, UpdateOCR
```

## SignService

Chức năng chính:

- Tạo Root CA nội bộ nếu chưa có.
- Cấp certificate cho user.
- Cho `Manager` và `BoardOfDirectors` tự tạo certificate cho chính tài khoản bằng danh tính trong JWT; Admin vẫn quản lý tập trung.
- Ký nháy: `PersonalSignature`, dành cho `Manager` hoặc `Admin`.
- Ký pháp nhân: `LegalSeal`, dành cho `BoardOfDirectors` hoặc `Admin`, yêu cầu đã có chữ ký nháy.
- Verify chữ ký trên PDF.

Lưu ý tích hợp hiện tại:

- DocumentService upload file theo tên GUID ngẫu nhiên và lưu `MinioPath = "documents/{storedFileName}"`.
- SignService đọc `Documents.MinioPath` từ PostgreSQL qua projection read-only, chuẩn hóa bỏ prefix bucket `documents/`, rồi tải/lưu lại đúng object thật trên MinIO.
- Luồng ký thực tế đã được test qua Docker/Gateway với path dạng `documents/<guid>.pdf` trong test case `TC-SIGN-001`.

## OCRService

Tech stack:

- Python FastAPI.
- PaddleOCR tiếng Việt.
- pdf2image/Poppler để chuyển PDF sang ảnh.
- MinIO để tải file PDF.
- Kafka consumer tùy chọn.

Endpoints:

| Method | Path | Mô tả |
|---|---|---|
| POST | `/api/ocr/process` | OCR từ MinIO path |
| POST | `/api/ocr/process-upload` | Upload PDF và OCR trực tiếp để test |
| GET | `/api/ocr/health` | Health check |

Luồng tự động đã có và được ghi nhận kiểm thử trong `TC-OCR-E2E-010` / `TC-OCR-SCAN-014`:

```text
DocumentService upload PDF
  -> lưu file vào MinIO
  -> publish Kafka event document.uploaded
  -> OCRService consume event
  -> tải PDF từ MinIO
  -> OCR + bóc tách trường
  -> PATCH /api/documents/{id}/ocr
```

Hiện `DocumentService` vẫn publish event với `token` rỗng. OCRService sẽ dùng `SERVICE_TOKEN` cấu hình trong môi trường để PATCH kết quả về DocumentService bằng header `X-Service-Token`.

## Frontend

Frontend là Blazor WebAssembly:

- Lưu JWT trong localStorage.
- `CustomAuthStateProvider` parse JWT claims.
- `ApiService.SmartDeserialize()` tự unwrap `ApiResponse<T>` nếu backend trả wrapper.
- Màn chi tiết công văn có link xem kết quả OCR tại `/documents/{id}/ocr`.
- Sửa metadata ở `Draft`/`Rejected` qua `PUT /api/documents/{id}` và ghi lịch sử `Update`; không thay file, OCR hoặc trạng thái.
- Xem/tải PDF qua API có JWT `GET /api/documents/{id}/file`, tải đúng `Documents.MinioPath`, rồi tạo blob URL trong trình duyệt; không dùng hostname MinIO nội bộ hoặc đưa JWT vào URL.
- Phân công gửi `ToUserId`; DocumentService gọi IdentityService bằng JWT hiện tại để kiểm tra người nhận hoạt động. Cấu hình `IdentityService:BaseUrl`: local `http://localhost:5048/`, Compose `http://identity-service:8080/`.
- Màn OCR đọc `OcrDataRaw`, hiển thị trường bóc tách, dòng text, raw JSON và lịch sử `UpdateOCR`.
- Màn ký số lấy `SignerId`/`SignerName` từ JWT và gửi đúng `SignRequestDto` backend.
- Có màn hình login, first login, forgot/reset password, dashboard, admin users/departments/certificates, documents, signatures và chứng thư của tôi.
- Có 17 page Razor trong `Frontend/Pages`, mỗi page có CSS isolation cùng tên.
- `/admin/system-logs` và `/admin/activity` đọc dữ liệu thật dành cho Admin: lọc service, mức độ, ngày Việt Nam, trace và actor, phân trang server. `/notifications` chỉ dữ liệu của user hiện tại, có lọc loại/chưa đọc, đánh dấu một/tất cả đã đọc, link chi tiết và polling 30 giây; lỗi nguồn có cảnh báo.
- Dashboard đọc thống kê thật từ ba API qua Gateway: `GET /api/documents/stats` (JWT), `GET /api/users/stats` và `GET /api/signatures/certificates/stats` (Admin). Frontend gọi các nguồn độc lập, giữ số liệu tải được; nguồn lỗi hoặc thiếu trường dữ liệu hiển thị `—` và cảnh báo, có nút làm mới.
- Thống kê công văn là aggregate SQL trên toàn dữ liệu, không phụ thuộc phân trang. Ngày hôm nay tính UTC+7; người tạo/thời điểm tạo lấy process `Submit` sớm nhất theo source hiện tại. Dự thảo/chờ phê duyệt của tôi và công văn đã phân công dùng actor trong JWT. Các số chờ ký theo cấp là toàn hệ thống, cùng phạm vi đọc danh sách công văn hiện tại.
- Viewport tối thiểu `450×500`; layout mobile rộng `450–960px`, desktop trên `960px`. Modal toàn màn hình khi rộng không quá `1200px` hoặc cao không quá `650px`.

Base API hiện trỏ đến Gateway:

```csharp
BaseAddress = new Uri("http://localhost:5000/")
```

## Hạ tầng

| Thành phần | Code hiện dùng |
|---|---|
| Database | PostgreSQL + EF Core 9/Npgsql |
| Identity schema | `EnsureCreatedAsync()` + `AuthStoreInitializer` bổ sung các bảng token và cột `EmailVerifiedAt` |
| Document schema | EF Core migrations, auto `MigrateAsync()` khi startup |
| Sign schema | EF Core migrations, auto `MigrateAsync()` khi startup |
| Object storage | MinIO bucket `documents` |
| Message broker | Kafka topic `document.uploaded` |
| SMTP local mặc định trong Compose | Mailpit; có thể override qua `.env` |
| Logging | Serilog |
| Test | xUnit, Moq, FluentAssertions |
