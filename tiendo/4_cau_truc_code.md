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
- Cấu hình YARP reverse proxy từ `ReverseProxy` section.
- Cấu hình rate limit bằng `AspNetCoreRateLimit`.
- Map `/health`, `/swagger`, `/`.
- Route cuối cùng bằng `app.MapReverseProxy()`.

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

DTO nhóm chính:

- Auth: login, refresh token, validate token, change password, forgot/reset password.
- Users: create/update/user dto.
- Roles.
- Departments.

Interfaces:

- Repository: `IUserRepository`, `IRoleRepository`, `IDepartmentRepository`, `IPasswordResetRepository`.
- Service: `IAuthService`, `IUserService`, `IRoleService`, `IDepartmentService`, `ITokenService`, `IEmailService`.

### Infrastructure

File chính:

- `Data/AppDbContext.cs`
- `Extensions/ServiceCollectionExtensions.cs`
- `Repositories/UserRepository.cs`
- `Repositories/RoleRepository.cs`
- `Repositories/DepartmentRepository.cs`
- `Repositories/PasswordResetRepository.cs`
- `Services/AuthService.cs`
- `Services/TokenService.cs`
- `Services/UserService.cs`
- `Services/RoleService.cs`
- `Services/DepartmentService.cs`
- `Services/EmailService.cs`

`AppDbContext` dùng `HasData()` để seed roles, departments và admin.

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
  -> UpdateOcrDto
  -> DocumentService.UpdateOcrDataAsync
  -> cập nhật DocNumber, Title, IssuedDate, OcrDataRaw
  -> tạo DocumentProcess action UpdateOCR
```

Workflow:

```text
SubmitForReviewAsync: Draft -> PendingDeptReview
DeptSignAsync: PendingDeptReview -> PendingDirectorSign
DirectorSignAsync: PendingDirectorSign -> DirectorSigned
PublishAsync: DirectorSigned -> Published
RejectAsync: pending states -> Rejected
AssignAsync: ghi DocumentProcess, không đổi status
```

`DocumentStatus.DeptSigned` có khai báo trong code và được cho phép reject, nhưng luồng hiện tại không dừng ở trạng thái này mà chuyển thẳng sang `PendingDirectorSign`.

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
  -> document_service.update_ocr_result(...)
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
- `Pages/Signatures/Index.razor`

## 8. Điểm cần chú ý khi sửa code

- Backend route hiện là `/api/...`, không dùng `/api/v1`.
- Gateway là YARP, config route nằm trong `ApiGateway/appsettings.json`.
- Identity dùng `EnsureCreatedAsync()`, Document/Sign dùng EF migrations.
- Frontend Docker build đã cài `wasm-tools`; nếu publish frontend trên máy host thì máy host cũng nên cài workload này.
- `ApiService.SmartDeserialize()` đã xử lý cả response trực tiếp và response bọc `ApiResponse<T>`.
- Luồng OCR tự động cần token hợp lệ để PATCH về DocumentService.
- Luồng ký số backend đã thống nhất MinIO object path bằng cách SignService đọc `Documents.MinioPath`.
- Frontend ký số hiện lấy `SignerId`/`SignerName` từ JWT và gửi `DocId`, `SignerId`, `SignerName`, `Reason` đúng `SignRequestDto` backend.
