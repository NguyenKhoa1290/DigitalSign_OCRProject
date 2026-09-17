# Kiến Trúc Dự Án - HAU DigitalSign OCR

> Tài liệu này mô tả kiến trúc theo code hiện tại trong repository.

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
F:\DigitalSign_OCRProject\
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
| ApiGateway | 5000 | Reverse proxy, JWT validation, rate limiting |
| IdentityService | 5048 | Đăng nhập, JWT, user, role, phòng ban, OTP reset |
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

Gateway hiện dùng YARP, không dùng Ocelot. Route hiện là `/api/...`, không có prefix `/api/v1`.

## IdentityService

Kiến trúc Clean Architecture:

- `Core`: entity, DTO, interface, exception.
- `Infrastructure`: EF Core, repository, service, JWT, BCrypt, email.
- `API`: controller, middleware, Swagger, auth pipeline.

Chức năng chính:

- Login, logout, refresh token, validate token.
- Đổi mật khẩu, bắt đổi mật khẩu lần đầu bằng `MustChangePassword`.
- Quên mật khẩu qua OTP email, OTP lưu SHA-256 hash.
- CRUD user, gán/gỡ role.
- CRUD department và cây phòng ban, có chống vòng lặp parent-child.

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
  -> DeptSigned (đã khai báo trong code)
  -> PendingDirectorSign
  -> DirectorSigned
  -> Published
```

Lưu ý: implementation hiện tại của `DeptSignAsync` đang chuyển thẳng từ `PendingDeptReview` sang `PendingDirectorSign`; `DeptSigned` tồn tại trong status constants nhưng chưa được dùng như trạng thái dừng riêng.

Từ chối:

```text
PendingDeptReview / DeptSigned / PendingDirectorSign -> Rejected
```

Các action log:

```text
Submit, DeptSign, DirectorSign, Reject, Publish, Assign, UpdateOCR
```

## SignService

Chức năng chính:

- Tạo Root CA nội bộ nếu chưa có.
- Cấp certificate cho user.
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

Luồng tự động dự kiến:

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
- Màn ký số lấy `SignerId`/`SignerName` từ JWT và gửi đúng `SignRequestDto` backend.
- Có màn hình login, first login, forgot/reset password, dashboard, admin users/departments/certificates, documents, signatures.

Base API hiện trỏ đến Gateway:

```csharp
BaseAddress = new Uri("http://localhost:5000/")
```

## Hạ tầng

| Thành phần | Code hiện dùng |
|---|---|
| Database | PostgreSQL + EF Core 9/Npgsql |
| Identity schema | `EnsureCreatedAsync()` |
| Document schema | EF Core migrations, auto `MigrateAsync()` khi startup |
| Sign schema | EF Core migrations, auto `MigrateAsync()` khi startup |
| Object storage | MinIO bucket `documents` |
| Message broker | Kafka topic `document.uploaded` |
| Logging | Serilog |
| Test | xUnit, Moq, FluentAssertions |
