# Cơ Sở Hạ Tầng & Cơ Sở Dữ Liệu

> Cập nhật theo cấu hình và code hiện tại.

## Tổng quan hạ tầng

| Thành phần | Công nghệ | Vai trò |
|---|---|---|
| Database | PostgreSQL + EF Core 9 + Npgsql | Lưu metadata nghiệp vụ |
| Object storage | MinIO | Lưu file PDF |
| Message broker | Kafka | Event `document.uploaded` cho OCR |
| Auth | JWT Bearer HS256 | IdentityService phát hành, Gateway/service validate |
| Password hash | BCrypt.Net | Hash mật khẩu user |
| Email | MailKit + MimeKit 4.18.0 | Gửi OTP reset password |
| PDF signing | iText7 + BouncyCastle | Ký và verify PDF |

Root repository có `docker-compose.yml` để chạy full stack local bằng Docker:

```bash
docker compose up -d
```

Nếu chỉ muốn bật hạ tầng để debug service bằng `dotnet run`/`uvicorn`, dùng:

```bash
docker compose up -d postgres minio kafka minio-init
```

Host dev theo cấu hình local Docker hiện tại:

```text
PostgreSQL: localhost:5432
Database:   DigitalSign_OCR
MinIO:      localhost:9000
MinIO UI:   localhost:9001
Kafka:      localhost:9092
```

Các volume dữ liệu quan trọng:

| Volume | Nội dung |
|---|---|
| `hau_postgres_data` | Database PostgreSQL |
| `hau_minio_data` | Object/file trong MinIO |
| `hau_kafka_data` | Dữ liệu Kafka local |
| `hau_sign_certs` | Root CA và PFX user của SignService |

Không đưa secret thật vào tài liệu public/deploy. Các file `appsettings*.json` hiện vẫn có giá trị dev.
Khi chạy bằng Docker Compose, copy `.env.example` ở root thành `.env` và đổi secret thật tại đó.
`docker-compose.yml` dùng `${VAR:-default_dev}` nên local/dev vẫn chạy được nếu chưa tạo `.env`.

## Chiến lược schema theo service

| Service | DbContext | Strategy hiện tại |
|---|---|---|
| IdentityService | `IdentityService.Infrastructure.Data.AppDbContext` | `EnsureCreatedAsync()` khi startup |
| DocumentService | `DocumentService.Infrastructure.Data.AppDbContext` | EF Core migrations + `MigrateAsync()` khi startup |
| SignService | `SignService.Infrastructure.Data.AppDbContext` | EF Core migrations + `MigrateAsync()` khi startup |

Lưu ý quan trọng:

- `EnsureCreatedAsync()` không cập nhật schema khi entity thay đổi sau lần tạo DB đầu tiên.
- Nếu IdentityService đã tạo DB rồi mà thêm cột/bảng mới, cần chạy SQL thủ công, chuyển sang migrations, hoặc dùng initializer bổ sung có kiểm soát.
- Hiện IdentityService có `AuthStoreInitializer.EnsureAuthTablesAsync()` để tạo thêm `RefreshTokens` và `RevokedAccessTokens` trên PostgreSQL Docker/local đã tồn tại.
- DocumentService và SignService đã có migration folder và startup tự apply migration.

## IdentityService database

### DbSets

```csharp
DbSet<AppUser> AppUsers
DbSet<AppRole> AppRoles
DbSet<AppUserRole> AppUserRoles
DbSet<Department> Departments
DbSet<PasswordResetToken> PasswordResetTokens
DbSet<RefreshToken> RefreshTokens
DbSet<RevokedAccessToken> RevokedAccessTokens
```

### Bảng `AppUsers`

| Column | Ghi chú |
|---|---|
| `Id` | UUID PK |
| `Username` | unique, required, max 50 |
| `PasswordHash` | required, max 256 |
| `FullName` | required, max 100 |
| `Email` | nullable, unique filtered index |
| `PhoneNumber` | nullable, max 15 |
| `DepartmentId` | FK nullable sang `Departments`, delete set null |
| `IsActive` | default true |
| `MustChangePassword` | default false |
| `CreatedAt` | UTC timestamp |

### Bảng `AppRoles`

| Column | Ghi chú |
|---|---|
| `Id` | UUID PK |
| `RoleName` | unique, required, max 50 |
| `Description` | nullable, max 255 |

Seed roles:

```text
Admin
Clerk
Specialist
Manager
BoardOfDirectors
```

### Bảng `AppUserRoles`

- Composite primary key: `(UserId, RoleId)`.
- `UserId` cascade delete.
- `RoleId` cascade delete.

### Bảng `Departments`

| Column | Ghi chú |
|---|---|
| `Id` | UUID PK |
| `DeptName` | required, max 150 |
| `DeptCode` | unique, required, max 20 |
| `ParentId` | FK self-reference, nullable |
| `Description` | nullable, max 500 |
| `CreatedAt` | UTC timestamp |

Self-reference dùng `DeleteBehavior.Restrict` để tránh xóa dây chuyền cả cây.

### Bảng `PasswordResetTokens`

| Column | Ghi chú |
|---|---|
| `Id` | UUID PK |
| `UserId` | FK required sang `AppUsers`, cascade delete |
| `TokenHash` | SHA-256 hex, max 64 |
| `ExpiresAt` | thời điểm hết hạn |
| `IsUsed` | default false |
| `CreatedAt` | UTC timestamp |

Index:

```text
IX_PasswordResetTokens_UserId_IsUsed
```

### Bảng `RefreshTokens`

| Column | Ghi chú |
|---|---|
| `Id` | UUID PK |
| `UserId` | FK required sang `AppUsers`, cascade delete |
| `TokenHash` | SHA-256 hex của refresh token, unique, max 64 |
| `AccessTokenJti` | `jti` của access token tương ứng, max 64 |
| `ExpiresAt` | thời điểm refresh token hết hạn |
| `CreatedAt` | UTC timestamp |
| `RevokedAt` | nullable; có giá trị khi token đã logout/rotate |
| `ReplacedByTokenHash` | hash refresh token mới khi rotate, nullable |

Index:

```text
IX_RefreshTokens_TokenHash
IX_RefreshTokens_UserId_RevokedAt_ExpiresAt
```

### Bảng `RevokedAccessTokens`

| Column | Ghi chú |
|---|---|
| `Id` | UUID PK |
| `UserId` | FK required sang `AppUsers`, cascade delete |
| `Jti` | `jti` của access token đã logout, unique, max 64 |
| `ExpiresAt` | thời điểm access token hết hạn, dùng để biết thời hạn blacklist |
| `RevokedAt` | UTC timestamp khi logout |

Index:

```text
IX_RevokedAccessTokens_Jti
IX_RevokedAccessTokens_ExpiresAt
```

### Seed admin

| Field | Value |
|---|---|
| Username | `admin` |
| Password seed | BCrypt hash của `Admin@123` |
| Role | `Admin` |
| MustChangePassword | `false` |

## DocumentService database

### DbSets

```csharp
DbSet<Document> Documents
DbSet<DocumentType> DocumentTypes
DbSet<DocumentProcess> DocumentProcesses
```

### Bảng `Documents`

| Column | Ghi chú |
|---|---|
| `Id` | UUID PK |
| `DocNumber` | nullable, max 50, có index |
| `Title` | required, max 500 |
| `IssuedDate` | nullable |
| `MinioPath` | required, lưu path file trong MinIO |
| `OcrDataRaw` | `jsonb`, nullable |
| `Status` | required, max 50 |
| `DocTypeId` | FK sang `DocumentTypes`, restrict delete |

Status hiện dùng string constants:

```text
Draft
PendingDeptReview
DeptSigned
PendingDirectorSign
DirectorSigned
Published
Rejected
```

### Bảng `DocumentTypes`

| Column | Ghi chú |
|---|---|
| `Id` | UUID PK |
| `TypeName` | unique, required, max 100 |
| `Description` | nullable, max 255 |

Seed types:

```text
CongVanDen
CongVanDi
ToTrinh
QuyetDinh
```

### Bảng `DocumentProcesses`

| Column | Ghi chú |
|---|---|
| `Id` | UUID PK |
| `DocId` | FK sang `Documents`, cascade delete |
| `FromUserId` | user thực hiện |
| `ToUserId` | user nhận phân công, nullable |
| `Action` | required, max 50 |
| `Comment` | ghi chú |
| `Timestamp` | thời điểm xử lý |

Actions:

```text
Submit
DeptSign
DirectorSign
Reject
Publish
Assign
UpdateOCR
```

## SignService database

### DbSets

```csharp
DbSet<Signature> Signatures
DbSet<DocumentFileRecord> DocumentFiles
```

`DocumentFiles` là projection read-only của SignService sang bảng `Documents` do DocumentService sở hữu. Mapping dùng `ToTable("Documents", table => table.ExcludeFromMigrations())` để SignService đọc `MinioPath` theo `DocId` nhưng không tạo/đổi migration cho bảng này.

### Bảng `Signatures`

| Column | Ghi chú |
|---|---|
| `Id` | UUID PK |
| `DocId` | ID văn bản, có index |
| `SignerId` | ID người ký |
| `SignatureType` | required, max 100 |
| `SignedAt` | thời điểm ký |

Signature types:

```text
PersonalSignature
LegalSeal
```

Certificate user không lưu trong DB hiện tại; file PFX được tạo trong thư mục `certs/` theo cấu hình `CertificateSettings:CertsDirectory`.

Khi chạy bằng Docker, thư mục này được mount vào volume `hau_sign_certs` tại `/app/certs`. Nếu migrate dữ liệu sang máy khác, cần backup/restore volume này cùng PostgreSQL và MinIO; nếu mất Root CA/PFX user thì các chứng thư đã cấp không còn đồng bộ với dữ liệu cũ.

## MinIO

DocumentService:

- Bucket constant: `documents`.
- File upload bằng tên GUID ngẫu nhiên giữ extension gốc.
- `Document.MinioPath = "documents/{storedFileName}"`.
- Kafka event gửi `minio_path = storedFileName`.

SignService:

- Bucket đọc từ `MinioSettings:Bucket`, mặc định `documents`.
- Đọc `Document.MinioPath` từ bảng `Documents`.
- Chuẩn hóa các dạng `documents/{storedFileName}`, `/documents/{storedFileName}` hoặc URL tuyệt đối thành object name `{storedFileName}` trước khi gọi MinIO.
- Sau khi ký, upload đè lại cùng object để các service khác vẫn dùng đúng `Document.MinioPath`.

Quy ước hiện tại:

- DocumentService là nơi tạo/lưu path gốc.
- SignService chỉ đọc path đó và không tự suy đoán `{docId}.pdf`.

## Kafka

DocumentService producer:

- Interface: `IKafkaProducerService`.
- Implementation: `KafkaProducerService`.
- Topic mặc định: `document.uploaded`.
- Có thể tắt bằng `KafkaSettings:Enabled=false`.
- Nếu publish Kafka lỗi, upload file vẫn thành công.

Payload hiện tại:

```json
{
  "doc_id": "uuid",
  "minio_path": "stored-file-name.pdf",
  "token": "",
  "timestamp": "..."
}
```

OCRService consumer:

- Topic mặc định: `document.uploaded`.
- Consumer group: `ocr-service-group`.
- Chỉ khởi động nếu `kafka_enabled=true`.
- Gọi `process_document(doc_id, minio_path, token)`.
- Nếu `token` rỗng, OCRService dùng `SERVICE_TOKEN` để gọi DocumentService bằng header `X-Service-Token`.
- `SERVICE_TOKEN` phải khớp với `ServiceAuth:OcrServiceToken` của DocumentService.
- Consumer có retry 5 giây/lần khi Kafka chưa sẵn sàng hoặc kết nối lỗi, phù hợp môi trường Docker Compose khởi động nhiều container song song.
- Luồng upload PDF thật qua Kafka/PaddleOCR đã pass `TC-OCR-E2E-010`: DocumentService publish `document.uploaded`, OCRService xử lý PDF từ MinIO và PATCH `UpdateOCR` về DocumentService.

Service-to-service auth hiện tại:

- Docker compose đã cấu hình `ServiceAuth__OcrServiceToken` cho `document-service`.
- Docker compose đã cấu hình `SERVICE_TOKEN` cho `ocr-service`.
- `ServiceAuth:OcrServiceUserId = 00000000-0000-0000-0000-000000000051` được dùng làm actor hệ thống khi ghi `DocumentProcess` action `UpdateOCR`.

## JWT

Cấu hình chung trong các service:

```json
{
  "JwtSettings": {
    "Issuer": "IdentityService",
    "Audience": "HAU-MicroservicesClients"
  }
}
```

IdentityService là nơi phát hành token. Gateway, DocumentService và SignService vẫn validate JWT bằng signing key; riêng Gateway có thêm bước gọi IdentityService `/api/auth/validate-token` để kiểm tra blacklist/logout trước khi proxy request xuống downstream service.

Refresh/logout hiện tại:

- IdentityService lưu refresh token bằng SHA-256 hash trong bảng `RefreshTokens`.
- Mỗi lần refresh thành công sẽ revoke refresh token cũ và tạo refresh token mới.
- Logout revoke toàn bộ refresh token active của user và lưu `jti` access token vào `RevokedAccessTokens`.
- IdentityService tự kiểm tra blacklist trong `OnTokenValidated` và trong `POST /api/auth/validate-token`.
- ApiGateway validate JWT cục bộ trước, sau đó gọi IdentityService `/api/auth/validate-token`; token đã logout bị chặn 401 trước khi tới Document/Sign/OCR.
- Nếu IdentityService không sẵn sàng trong `AuthValidation:TimeoutSeconds`, Gateway fallback sang JWT local khi `AuthValidation:FailOpenOnValidationError=true`; cấu hình này tránh làm gián đoạn toàn bộ route downstream khi IdentityService tạm lỗi.

## Ghi chú vận hành

- Trước deploy, chuyển secret ra environment variables hoặc secret manager.
- Không commit PFX thật, app password Gmail, JWT key production.
- Không commit file `.env` thật; repo chỉ commit `.env.example`.
- `EmailService` cần `EmailSettings:Username` và `EmailSettings:Password`; nếu thiếu sẽ báo lỗi cấu hình rõ ràng trước khi gửi SMTP.
- Nếu chuyển IdentityService sang migrations, cần tạo migration đầu tiên cẩn thận vì DB dev có thể đã được tạo bằng `EnsureCreatedAsync()`.
