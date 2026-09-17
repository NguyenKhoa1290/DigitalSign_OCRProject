# HAU DigitalSign OCR

> Hệ thống quản lý công văn số hóa, OCR và ký số cho Trường Đại học Kiến Trúc Hà Nội.

## Trạng thái theo code hiện tại

| Thành phần | Công nghệ | Port dev | Trạng thái |
|---|---|---:|---|
| Frontend | Blazor WebAssembly .NET 9 | 5227 | Đã có màn hình auth, dashboard, admin, documents, signatures |
| API Gateway | ASP.NET Core + YARP | 5000 | Đã route JWT/rate limit đến các service |
| IdentityService | ASP.NET Core .NET 9, EF Core, PostgreSQL | 5048 | Đã có auth, user, role, department, OTP reset |
| DocumentService | ASP.NET Core .NET 9, EF Core migrations, MinIO, Kafka producer | 5049 | Đã có CRUD, upload, OCR update, workflow |
| SignService | ASP.NET Core .NET 9, iText7, BouncyCastle, MinIO | 5050 | Đã có cấp certificate, ký PDF, verify chữ ký |
| OCRService | Python FastAPI, PaddleOCR, pdf2image, MinIO, Kafka consumer | 5051 | Đã có OCR backend, chưa có màn hình OCR riêng |

## Kiến trúc tổng thể

```text
Blazor WebAssembly
        |
        v
API Gateway :5000 (YARP, JWT validation, rate limit)
        |
        +--> IdentityService :5048  -> PostgreSQL
        +--> DocumentService :5049  -> PostgreSQL + MinIO + Kafka producer
        +--> SignService     :5050  -> PostgreSQL + MinIO + local certs/
        +--> OCRService      :5051  -> MinIO + DocumentService PATCH /api/documents/{id}/ocr
```

## Route chính qua Gateway

| Path | Service đích | Auth |
|---|---|---|
| `/api/auth/**` | IdentityService | Anonymous |
| `/api/users/**` | IdentityService | JWT |
| `/api/roles/**` | IdentityService | JWT |
| `/api/departments/**` | IdentityService | JWT |
| `/api/documents/**` | DocumentService | JWT |
| `/api/signatures/**` | SignService | JWT |
| `/api/ocr/**` | OCRService | JWT |

Lưu ý: code hiện dùng route `/api/...`, không dùng `/api/v1/...`.

## Workflow văn bản

Workflow hiện tại nằm trong `DocumentService.Core.Entities.DocumentStatus`:

```text
Draft
  -> PendingDeptReview
  -> DeptSigned (trạng thái có khai báo trong code)
  -> PendingDirectorSign
  -> DirectorSigned
  -> Published
```

Lưu ý: code hiện tại khai báo `DeptSigned`, nhưng hàm `DeptSignAsync` đang chuyển thẳng từ `PendingDeptReview` sang `PendingDirectorSign`. Có nhánh `Rejected` khi từ chối ở các trạng thái pending. Action log nằm trong `DocumentProcess` với các action: `Submit`, `DeptSign`, `DirectorSign`, `Reject`, `Publish`, `Assign`, `UpdateOCR`.

## API chính

### IdentityService

```text
POST   /api/auth/login
POST   /api/auth/logout
POST   /api/auth/refresh-token
POST   /api/auth/validate-token
POST   /api/auth/change-password
POST   /api/auth/forgot-password
POST   /api/auth/reset-password

GET    /api/users
GET    /api/users/{id}
POST   /api/users
PUT    /api/users/{id}
DELETE /api/users/{id}
POST   /api/users/{id}/roles/{roleId}
DELETE /api/users/{id}/roles/{roleId}
GET    /api/users/me

GET    /api/roles
GET    /api/roles/{id}
GET    /api/departments
GET    /api/departments/tree
GET    /api/departments/{id}
GET    /api/departments/{id}/children
POST   /api/departments
PUT    /api/departments/{id}
DELETE /api/departments/{id}
```

### DocumentService

```text
GET    /api/documents
POST   /api/documents
GET    /api/documents/types
GET    /api/documents/{id}
DELETE /api/documents/{id}
POST   /api/documents/{id}/upload
PATCH  /api/documents/{id}/ocr
POST   /api/documents/{id}/submit
POST   /api/documents/{id}/dept-sign
POST   /api/documents/{id}/director-sign
POST   /api/documents/{id}/reject
POST   /api/documents/{id}/publish
POST   /api/documents/{id}/assign
```

### SignService

```text
POST   /api/signatures/personal-sign
POST   /api/signatures/legal-seal
GET    /api/signatures/document/{docId}
GET    /api/signatures/document/{docId}/verify
POST   /api/signatures/certificates/issue
GET    /api/signatures/certificates/{userId}
```

### OCRService

```text
POST   /api/ocr/process
POST   /api/ocr/process-upload
GET    /api/ocr/health
```

## Dữ liệu và hạ tầng

| Thành phần | Ghi chú |
|---|---|
| PostgreSQL | Docker local `localhost:5432`, database `DigitalSign_OCR` |
| MinIO | Bucket mặc định `documents` |
| Kafka | Topic `document.uploaded`, có thể tắt bằng cấu hình |
| JWT | Issuer `IdentityService`, audience `HAU-MicroservicesClients` |
| Password | BCrypt work factor 12 |
| OTP reset | SHA-256 hash, hết hạn 15 phút, dùng một lần |

## Ghi chú tích hợp quan trọng

- `DocumentService` upload file lên MinIO bằng tên GUID ngẫu nhiên và lưu `MinioPath = "documents/{storedFileName}"`.
- `DocumentService` publish Kafka event `document.uploaded` với `minio_path = storedFileName`; hiện `authToken` đang gửi rỗng.
- `OCRService` chỉ tự PATCH kết quả về `DocumentService` nếu request/Kafka event có token hợp lệ.
- `SignService` đọc `Documents.MinioPath`, bỏ prefix bucket `documents/` khi cần, rồi tải/lưu lại đúng object PDF trên MinIO. Luồng này đã pass test Docker/API `TC-SIGN-001`.
- Frontend ký số đã gửi đúng `SignRequestDto` backend (`DocId`, `SignerId`, `SignerName`, `Reason`) và payload này đã pass test Docker/API `TC-FE-SIGN-002`.

## Cách chạy nhanh

### Chạy toàn bộ bằng Docker

File `docker-compose.yml` ở root hiện chạy full stack: PostgreSQL, MinIO, Kafka, các backend service, Gateway và Frontend.

```bash
docker compose up -d
```

Frontend sẽ chạy ở `http://localhost:5227`, Gateway ở `http://localhost:5000`. Xem chi tiết trong `TRIEN_KHAI_DOCKER.md`.

### Chạy dev mixed: Docker chỉ bật hạ tầng

Nếu muốn chạy service bằng `dotnet run`/`uvicorn` để debug code, chỉ bật hạ tầng:

```bash
docker compose up -d postgres minio kafka minio-init
```

PostgreSQL sẽ chạy ở `localhost:5432`, MinIO API ở `localhost:9000`, MinIO Console ở `http://localhost:9001`, Kafka ở `localhost:9092`.

```bash
dotnet run --project ApiGateway/ApiGateway.csproj
dotnet run --project IdentityService/src/IdentityService.API/IdentityService.API.csproj
dotnet run --project DocumentService/src/DocumentService.API/DocumentService.API.csproj
dotnet run --project SignService/src/SignService.API/SignService.API.csproj
dotnet run --project Frontend/HauDocumentApp.csproj
```

```bash
cd OCRService
uvicorn app.main:app --host 0.0.0.0 --port 5051 --reload
```
