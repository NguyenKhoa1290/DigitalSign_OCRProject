# Cấu Trúc Code Theo Repository Hiện Tại

> Bản đồ nhanh các project, class và luồng chính. Tài liệu này ưu tiên đúng với code hiện có hơn là lịch sử phát triển.

## 1. Solution

File solution:

```text
HAU_DigitalSign_OCR.slnx
```

Project trong solution:

```text
ApiGateway/ApiGateway.csproj
Frontend/HauDocumentApp.csproj
IdentityService/src/IdentityService.API/IdentityService.API.csproj
IdentityService/src/IdentityService.Core/IdentityService.Core.csproj
IdentityService/src/IdentityService.Infrastructure/IdentityService.Infrastructure.csproj
DocumentService/src/DocumentService.API/DocumentService.API.csproj
DocumentService/src/DocumentService.Core/DocumentService.Core.csproj
DocumentService/src/DocumentService.Infrastructure/DocumentService.Infrastructure.csproj
SignService/src/SignService.API/SignService.API.csproj
SignService/src/SignService.Core/SignService.Core.csproj
SignService/src/SignService.Infrastructure/SignService.Infrastructure.csproj
```

Test project:

```text
IdentityService/tests/IdentityService.Tests
DocumentService/tests/DocumentService.Tests
SignService/tests/SignService.Tests
```

## 2. ApiGateway

File chính:

- `ApiGateway/Program.cs`
- `ApiGateway/appsettings.json`

Nhiệm vụ:

- Cấu hình JWT Bearer.
- Sau khi JWT local hợp lệ, gọi IdentityService `/api/auth/validate-token` để kiểm tra blacklist/logout.
- Cấu hình YARP reverse proxy từ `ReverseProxy` section.
- Cấu hình rate limit bằng `AspNetCoreRateLimit`.
- Map `/health`, `/swagger`, `/`.
- Route cuối cùng bằng `app.MapReverseProxy()`.

Auth validation:

```text
Authorization: Bearer <jwt>
  -> Gateway validate issuer/audience/signature/expiry local
  -> OnTokenValidated gọi AuthValidation:ValidateTokenUrl
  -> IdentityService /api/auth/validate-token kiểm tra RevokedAccessTokens
  -> nếu isValid=false thì Gateway trả 401, không proxy xuống downstream service
  -> nếu IdentityService lỗi/timeout và FailOpenOnValidationError=true thì Gateway fallback sang JWT local
```

Config liên quan:

- `JwtSettings:*`: phải khớp với IdentityService.
- `AuthValidation:ValidateTokenUrl`: local mặc định `http://localhost:5048/api/auth/validate-token`.
- Docker compose override `AuthValidation__ValidateTokenUrl` sang `http://identity-service:8080/api/auth/validate-token`.
- `AuthValidation:TimeoutSeconds`: timeout gọi IdentityService.
- `AuthValidation:FailOpenOnValidationError`: nếu `true`, lỗi/timeout khi gọi IdentityService không làm rớt toàn bộ request có JWT hợp lệ local.

Route YARP nằm trong `appsettings.json`:

| Route | Cluster |
|---|---|
| `/api/auth/{**catch-all}` | `identity-cluster` |
| `/api/users/{**catch-all}` | `identity-cluster` |
| `/api/roles/{**catch-all}` | `identity-cluster` |
| `/api/departments/{**catch-all}` | `identity-cluster` |
| `/api/documents/{**catch-all}` | `document-cluster` |
| `/api/signatures/{**catch-all}` | `sign-cluster` |
| `/api/ocr/{**catch-all}` | `ocr-cluster` |

## 3. IdentityService

### Core

Entities:

- `AppUser`
- `AppRole`
- `AppUserRole`
- `Department`
- `PasswordResetToken`
- `RefreshToken`
- `RevokedAccessToken`

DTO nhóm chính:

- Auth: login, refresh token, validate token, change password, forgot/reset password.
- Users: create/update/user dto.
- Roles.
- Departments.

Interfaces:

- Repository: `IUserRepository`, `IRoleRepository`, `IDepartmentRepository`, `IPasswordResetRepository`, `IRefreshTokenRepository`, `IRevokedAccessTokenRepository`.
- Service: `IAuthService`, `IUserService`, `IRoleService`, `IDepartmentService`, `ITokenService`, `IEmailService`.

### Infrastructure

File chính:

- `Data/AppDbContext.cs`
- `Extensions/ServiceCollectionExtensions.cs`
- `Repositories/UserRepository.cs`
- `Repositories/RoleRepository.cs`
- `Repositories/DepartmentRepository.cs`
- `Repositories/PasswordResetRepository.cs`
- `Repositories/RefreshTokenRepository.cs`
- `Repositories/RevokedAccessTokenRepository.cs`
- `Services/AuthService.cs`
- `Services/TokenService.cs`
- `Services/UserService.cs`
- `Services/RoleService.cs`
- `Services/DepartmentService.cs`
- `Services/EmailService.cs`
- `Data/AuthStoreInitializer.cs`

`AppDbContext` dùng `HasData()` để seed roles, departments và admin.

`EmailService` đọc `EmailSettings` và hỗ trợ cả SMTP thật có auth/TLS lẫn SMTP local Mailpit không auth:

- `SmtpHost`, `SmtpPort`
- `SecureSocketOptions`
- `RequireAuth`
- `Username`, `Password`
- `FromEmail`, `FromName`

Luồng token hiện tại:

```text
POST /api/auth/login
  -> AuthService.LoginAsync
  -> phát access token + refresh token
  -> lưu SHA-256(refresh token) vào RefreshTokens kèm AccessTokenJti

POST /api/auth/refresh-token
  -> parse access token kể cả khi hết hạn
  -> kiểm tra refresh token active trong DB
  -> revoke refresh token cũ
  -> tạo access token + refresh token mới

POST /api/auth/logout
  -> revoke toàn bộ refresh token active của user
  -> lưu jti access token vào RevokedAccessTokens nếu token chưa hết hạn

POST /api/auth/validate-token
  -> validate JWT
  -> kiểm tra jti có nằm trong RevokedAccessTokens hay không
```

### API

Controllers:

- `AuthController`: `/api/auth`
- `UsersController`: `/api/users`
- `RolesController`: `/api/roles`
- `DepartmentsController`: `/api/departments`

Middleware:

- `ExceptionHandlingMiddleware`

Startup:

- `Program.cs` gọi `AddInfrastructure()`.
- Auth pipeline: `UseAuthentication()`, `UseAuthorization()`.
- Database init: `EnsureCreatedAsync()` nếu không phải môi trường `Testing`.
- Sau `EnsureCreatedAsync()`, `AuthStoreInitializer.EnsureAuthTablesAsync()` tạo bổ sung `RefreshTokens` và `RevokedAccessTokens` cho DB PostgreSQL đã tồn tại từ trước.
- JWT bearer event `OnTokenValidated` kiểm tra `RevokedAccessTokens` để chặn access token đã logout trong phạm vi IdentityService.
- ApiGateway cũng gọi `/api/auth/validate-token`, nên blacklist có hiệu lực với route Document/Sign/OCR đi qua Gateway.

## 4. DocumentService

### Core

Entities:

- `Document`
- `DocumentType`
- `DocumentProcess`
- `DocumentStatus`
- `DocumentAction`

DTO:

- `CreateDocumentDto`
- `DocumentDto`
- `DocumentProcessDto`
- `DocumentQueryParams`
- `DocumentTypeDto`
- `RejectDocumentDto`
- `UpdateOcrDto`
- `WorkflowActionDto`

Interfaces:

- `IDocumentService`
- `IDocumentRepository`
- `IDocumentTypeRepository`
- `IDocumentProcessRepository`
- `IFileStorageService`
- `IKafkaProducerService`

### Infrastructure

File chính:

- `Data/AppDbContext.cs`
- `Extensions/ServiceCollectionExtensions.cs`
- `Repositories/DocumentRepository.cs`
- `Repositories/DocumentTypeRepository.cs`
- `Repositories/DocumentProcessRepository.cs`
- `Services/DocumentService.cs`
- `Services/MinioStorageService.cs`
- `Services/KafkaProducerService.cs`

Luồng upload:

```text
DocumentsController.UploadFile
  -> DocumentService.UploadFileAsync
  -> MinioStorageService.UploadFileAsync
  -> document.MinioPath = "documents/{storedFileName}"
  -> KafkaProducerService.PublishDocumentUploadedAsync(...)
```

Luồng OCR update:

```text
PATCH /api/documents/{id}/ocr
  -> xác thực bằng JWT hoặc header X-Service-Token
  -> UpdateOcrDto
  -> DocumentService.UpdateOcrDataAsync
  -> cập nhật DocNumber, Title, IssuedDate, OcrDataRaw
  -> tạo DocumentProcess action UpdateOCR
```

Workflow:

```text
SubmitForReviewAsync: Draft -> PendingDeptReview
DeptSignAsync: PendingDeptReview -> DeptSigned
SubmitToDirectorAsync: DeptSigned -> PendingDirectorSign
DirectorSignAsync: PendingDirectorSign -> DirectorSigned
PublishAsync: DirectorSigned -> Published
RejectAsync: pending states -> Rejected
AssignAsync: ghi DocumentProcess, không đổi status
```

`DocumentStatus.DeptSigned` là trạng thái dừng sau khi lãnh đạo phòng ký nháy. Từ trạng thái này có thể reject hoặc gọi `SubmitToDirectorAsync` để chuyển sang `PendingDirectorSign`.

### API

Controller:

- `DocumentsController`: `/api/documents`

Startup:

- `Program.cs` gọi `AddInfrastructure()`.
- Auth bằng JWT Bearer.
- Tự chạy `db.Database.MigrateAsync()`.
- Có global exception handler map lỗi domain sang HTTP status.

## 5. SignService

### Core

Entities:

- `Signature`
- `SignatureType`
- `UserCertificate`

DTO:

- `SignRequestDto`
- `SignResultDto`
- `SignatureDto`
- `VerifyResultDto`
- `IssueCertificateDto`
- `CertificateDto`

Interfaces:

- `ISignService`
- `ISignatureRepository`
- `IDocumentFileRepository`
- `ICertificateService`
- `IPdfSigningService`
- `IMinioService`

### Infrastructure

File chính:

- `Data/AppDbContext.cs`
- `Data/DocumentFileRecord.cs`
- `Extensions/ServiceCollectionExtensions.cs`
- `Repositories/SignatureRepository.cs`
- `Repositories/DocumentFileRepository.cs`
- `Services/SignService.cs`
- `Services/PdfSigningService.cs`
- `Services/CertificateService.cs`
- `Services/MinioService.cs`

Luồng ký:

```text
SignaturesController.PersonalSign / LegalSeal
  -> SignService.PersonalSignAsync / LegalSealAsync
  -> kiểm tra certificate user
  -> kiểm tra chữ ký đã tồn tại chưa
  -> đọc Documents.MinioPath theo DocId
  -> chuẩn hóa "documents/{storedFileName}" thành object "{storedFileName}"
  -> tải PDF từ MinIO theo object thật
  -> PdfSigningService.SignPdfAsync
  -> upload lại PDF đã ký
  -> lưu Signature vào PostgreSQL
```

Luồng certificate:

```text
Program.cs startup
  -> CertificateService.InitializeRootCaAsync()
  -> tạo certs/rootca.pfx nếu chưa có

POST /api/signatures/certificates/issue
  -> CertificateService.IssueCertificateAsync
  -> tạo PFX user trong certs/{userId}.pfx
```

### API

Controller:

- `SignaturesController`: `/api/signatures`

Authorization:

- `personal-sign`: `Manager,Admin`
- `legal-seal`: `BoardOfDirectors,Admin`
- `certificates/issue`: `Admin`

Startup:

- Tự chạy EF migration.
- Tự khởi tạo Root CA.
- Auth bằng JWT Bearer.

## 6. OCRService

File chính:

- `app/main.py`
- `app/config.py`
- `app/api/routes.py`
- `app/ocr/engine.py`
- `app/ocr/extractor.py`
- `app/services/ocr_processor.py`
- `app/services/minio_service.py`
- `app/services/document_service.py`
- `app/services/kafka_consumer.py`

Luồng REST:

```text
POST /api/ocr/process
  -> ocr_processor.process_document(doc_id, minio_path, token)
  -> minio_service.download_file(minio_path)
  -> pdf2image.convert_from_bytes
  -> OcrEngine.extract_lines
  -> extractor.extract_fields
  -> document_service.update_ocr_result(...) bằng JWT hoặc SERVICE_TOKEN
```

Luồng upload test:

```text
POST /api/ocr/process-upload
  -> nhận UploadFile
  -> convert PDF bytes sang ảnh
  -> OCR
  -> trả OcrResponse
```

Kafka:

- `kafka_consumer.start_consumer(process_fn)` chạy background thread.
- Topic mặc định: `document.uploaded`.
- Payload kỳ vọng: `{ "doc_id": "...", "minio_path": "...", "token": "..." }`.
- Nếu `token` rỗng, `ocr_processor` dùng `settings.service_token` và `document_service.py` gửi header `X-Service-Token`.
- Consumer có retry loop khi Kafka chưa sẵn sàng/lỗi kết nối, không để thread chết hẳn lúc container khởi động lệch nhịp.
- `app/main.py` giữ event loop FastAPI lifespan và truyền coroutine OCR từ Kafka thread bằng `asyncio.run_coroutine_threadsafe(..., loop)`.

## 7. Frontend

File chính:

- `Program.cs`: đăng ký DI, HTTP client, auth, service.
- `Dockerfile`: build Blazor WebAssembly bằng .NET SDK, cài `python3` + `wasm-tools`, sau đó serve static files bằng Nginx.
- `App.razor`: `AuthorizeRouteView`.
- `Auth/CustomAuthStateProvider.cs`: parse JWT từ localStorage.
- `Services/ApiService.cs`: GET/POST/PUT/PATCH/DELETE + SmartDeserialize.
- `Services/AuthService.cs`: login/logout/change/forgot/reset password.
- `Services/AdminService.cs`: user, department, role, certificate.
- `Services/DocumentService.cs`: document workflow.
  - `RunOcrAsync` gọi `api/ocr/process` qua Gateway, lấy object name từ `DocumentDto.MinioPath`.
- `Services/SignatureService.cs`: signature API, gửi `SignRequestDto` đúng backend và map thao tác UI sang endpoint ký.

Pages:

- `Pages/Login.razor`
- `Pages/FirstLogin.razor`
- `Pages/ForgotPassword.razor`
- `Pages/ResetPassword.razor`
- `Pages/Dashboard.razor`
- `Pages/Admin/Users.razor`
- `Pages/Admin/Departments.razor`
- `Pages/Admin/Certificates.razor`
- `Pages/Documents/Index.razor`
- `Pages/Documents/Create.razor`
- `Pages/Documents/Detail.razor`
- `Pages/Documents/OcrResult.razor`
- `Pages/Signatures/Index.razor`

## 8. Điểm cần chú ý khi sửa code

- Backend route hiện là `/api/...`, không dùng `/api/v1`.
- Gateway là YARP, config route nằm trong `ApiGateway/appsettings.json`.
- Identity dùng `EnsureCreatedAsync()`, Document/Sign dùng EF migrations.
- Frontend Docker build đã cài `wasm-tools`; nếu publish frontend trên máy host thì máy host cũng nên cài workload này.
- `ApiService.SmartDeserialize()` đã xử lý cả response trực tiếp và response bọc `ApiResponse<T>`.
- Frontend `DocumentDto` có alias để tương thích backend hiện tại: `DocNumber`/`DocumentNumber`, `DocTypeName`/`DocumentTypeName`, `OcrDataRaw`/`OcrText`, `Processes`/`ProcessHistory`.
- Luồng OCR tự động dùng JWT nếu Kafka/request có token; nếu token rỗng thì dùng `SERVICE_TOKEN` để PATCH về DocumentService.
- Luồng ký số backend đã thống nhất MinIO object path bằng cách SignService đọc `Documents.MinioPath`.
- Frontend ký số hiện lấy `SignerId`/`SignerName` từ JWT và gửi `DocId`, `SignerId`, `SignerName`, `Reason` đúng `SignRequestDto` backend.
