# Test Cases

> Ghi lại test case đã chạy theo quy trình: viết code → build code → Docker → test case → ghi test case → báo cáo.

## TC-SIGN-001 — SignService dùng đúng `Documents.MinioPath` khi ký PDF

| Mục | Nội dung |
|---|---|
| Ngày chạy | 16/09/2026 |
| Phạm vi | Backend SignService + DocumentService + IdentityService qua Docker/Gateway |
| Mục tiêu | Xác nhận SignService không còn tự tìm `{docId}.pdf`, mà đọc `Documents.MinioPath` và tải đúng object PDF trên MinIO |
| Kết quả | Pass |

### Điều kiện trước test

- Docker stack đang chạy.
- PostgreSQL, MinIO, IdentityService, DocumentService, SignService, ApiGateway hoạt động.
- Có seed user `admin / Admin@123`.

### Các bước đã chạy

1. Login qua Gateway: `POST http://localhost:5000/api/auth/login`.
2. Lấy document type: `GET http://localhost:5000/api/documents/types`.
3. Tạo document test: `POST http://localhost:5000/api/documents`.
4. Tạo PDF test tạm trong `%TEMP%`.
5. Upload PDF: `POST http://localhost:5000/api/documents/{docId}/upload`.
6. Cấp certificate cho admin: `POST http://localhost:5000/api/signatures/certificates/issue`.
7. Ký nháy: `POST http://localhost:5000/api/signatures/personal-sign`.
8. Verify chữ ký: `GET http://localhost:5000/api/signatures/document/{docId}/verify`.
9. Kiểm tra log `hau_sign_service`.

### Dữ liệu/kết quả chính

| Trường | Giá trị |
|---|---|
| `DocId` | `68ca7361-149e-42b9-b8ec-da9b1f040a4e` |
| `UploadedMinioPath` | `documents/c7e57808-de01-461a-8e0a-578c24d88bdb.pdf` |
| `SignatureId` | `dc468d0b-5fc9-41c2-831e-51ac06e28529` |
| Verify `isValid` | `true` |
| Verify `totalSignatures` | `1` |

### Bằng chứng log

SignService log đã resolve đúng:

```text
documents/c7e57808-de01-461a-8e0a-578c24d88bdb.pdf -> c7e57808-de01-461a-8e0a-578c24d88bdb.pdf
```

### Build/test liên quan

| Lệnh | Kết quả |
|---|---|
| `dotnet build .\HAU_DigitalSign_OCR.slnx` | Pass; cảnh báo `NU1902` đã được xử lý ở `TC-DEPS-003` |
| `dotnet test .\HAU_DigitalSign_OCR.slnx --no-build` | Pass 29/29 |
| `docker compose build identity-service sign-service` | Pass |
| `docker compose up -d identity-service sign-service` | Pass, container chạy lại thành công |

### Ghi chú

- Lần chạy API đầu bị dừng trước bước upload do PowerShell 5.1 chưa load `System.Net.Http`; đã chạy lại với `Add-Type -AssemblyName System.Net.Http` và pass.
- Test tạo dữ liệu thật trong DB/MinIO Docker local. Dữ liệu này không ảnh hưởng code, nhưng vẫn nằm trong volume Docker hiện tại.

## TC-FE-SIGN-002 — Payload frontend ký số khớp `SignRequestDto` backend

| Mục | Nội dung |
|---|---|
| Ngày chạy | 16/09/2026 |
| Phạm vi | Frontend DTO/service + SignService API qua Docker/Gateway |
| Mục tiêu | Xác nhận payload mới của frontend có `DocId`, `SignerId`, `SignerName`, `Reason` và backend ký được cả `personal-sign` lẫn `legal-seal` |
| Kết quả | Pass |

### Điều kiện trước test

- Docker stack đang chạy.
- Frontend container đã được build/recreate từ code mới.
- PostgreSQL, MinIO, IdentityService, DocumentService, SignService, ApiGateway hoạt động.
- Có seed user `admin / Admin@123`.

### Các bước đã chạy

1. Build solution: `dotnet build .\HAU_DigitalSign_OCR.slnx`.
2. Chạy test .NET: `dotnet test .\HAU_DigitalSign_OCR.slnx --no-build`.
3. Build frontend image: `docker compose build frontend`.
4. Recreate frontend: `docker compose up -d frontend`.
5. Kiểm tra frontend: `Invoke-WebRequest http://localhost:5227`.
6. Login qua Gateway: `POST http://localhost:5000/api/auth/login`.
7. Tạo document test và upload PDF.
8. Cấp certificate cho admin.
9. Gọi `POST /api/signatures/personal-sign` bằng payload giống frontend mới:

```json
{
  "DocId": "uuid",
  "SignerId": "33333333-0000-0000-0000-000000000001",
  "SignerName": "System Administrator",
  "Reason": "TC-FE-SIGN-002 frontend personal-sign payload",
  "SignatureType": "DeptSign",
  "PinCode": null,
  "Comment": "frontend personal sign comment"
}
```

10. Gọi `POST /api/signatures/legal-seal` bằng payload giống frontend mới với `SignatureType = "DirectorSign"`.
11. Verify chữ ký: `GET /api/signatures/document/{docId}/verify`.

### Dữ liệu/kết quả chính

| Trường | Giá trị |
|---|---|
| `DocId` | `53b45b16-6898-46a2-95af-f3c372e1744e` |
| `UploadedMinioPath` | `documents/c8706004-aa77-4dcb-97b0-ca0c5b1d26cd.pdf` |
| `PersonalSignatureId` | `2a572a2b-81ba-4a52-ba9e-008bb4b94561` |
| `LegalSignatureId` | `49286c39-f890-4163-b53a-0e2945191ec9` |
| Verify `isValid` | `true` |
| Verify `totalSignatures` | `2` |
| Verify `hasPersonalSignature` | `true` |
| Verify `hasLegalSeal` | `true` |

### Build/test liên quan

| Lệnh | Kết quả |
|---|---|
| `dotnet build .\HAU_DigitalSign_OCR.slnx` | Pass; các warning đã được xử lý ở lần build sau |
| `dotnet test .\HAU_DigitalSign_OCR.slnx --no-build` | Pass 29/29 |
| `docker compose build frontend` | Pass; cảnh báo thiếu `wasm-tools` đã được xử lý ở `TC-DEPS-003` |
| `docker compose up -d frontend` | Pass |
| `Invoke-WebRequest http://localhost:5227` | HTTP 200 |

### Ghi chú

- Test này gọi API trực tiếp bằng payload giống model frontend mới để xác nhận backend nhận đúng DTO sau khi sửa.
- Cần kiểm thử UI thủ công trên trình duyệt nếu muốn xác nhận thao tác click modal ký số với tài khoản `Manager`/`BoardOfDirectors` thật.

## TC-DEPS-003 — Nâng MailKit/MimeKit và cài wasm-tools cho Docker frontend

| Mục | Nội dung |
|---|---|
| Ngày chạy | 16/09/2026 |
| Phạm vi | IdentityService email dependency + Frontend Docker build |
| Mục tiêu | Xử lý cảnh báo bảo mật `NU1902` của `MailKit`/`MimeKit` và cài `wasm-tools` để publish Blazor WASM tối ưu trong Docker |
| Kết quả | Pass |

### Các thay đổi chính

- `MailKit`: `4.8.0` → `4.18.0`.
- `MimeKit`: `4.8.0` → `4.18.0`.
- `EmailService` validate `EmailSettings:Username` và `EmailSettings:Password` trước khi gửi mail.
- Local SDK đã cài workload `wasm-tools`.
- `Frontend/Dockerfile` cài `python3` và `wasm-tools` trong build stage.

### Các bước đã chạy

1. Cập nhật package:

```powershell
dotnet add .\IdentityService\src\IdentityService.Infrastructure\IdentityService.Infrastructure.csproj package MailKit
dotnet add .\IdentityService\src\IdentityService.Infrastructure\IdentityService.Infrastructure.csproj package MimeKit
```

2. Kiểm tra vulnerability:

```powershell
dotnet list .\IdentityService\src\IdentityService.Infrastructure\IdentityService.Infrastructure.csproj package --vulnerable --include-transitive
```

3. Cài workload local:

```powershell
dotnet workload install wasm-tools
dotnet workload list
```

4. Build/test solution:

```powershell
dotnet build .\HAU_DigitalSign_OCR.slnx
dotnet test .\HAU_DigitalSign_OCR.slnx --no-build
```

5. Build/redeploy Docker:

```powershell
docker compose build identity-service frontend
docker compose up -d identity-service frontend
```

6. Health/login check:

```powershell
Invoke-WebRequest http://localhost:5048/health -UseBasicParsing
Invoke-WebRequest http://localhost:5227 -UseBasicParsing
POST http://localhost:5000/api/auth/login
```

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| `dotnet workload list` | Có `wasm-tools` |
| NuGet vulnerable packages | Không còn package vulnerable trong `IdentityService.Infrastructure` |
| `dotnet build .\HAU_DigitalSign_OCR.slnx` | Pass 0 warning/0 error |
| `dotnet test .\HAU_DigitalSign_OCR.slnx --no-build` | Pass 29/29 |
| `docker compose build identity-service frontend` | Pass |
| `docker compose up -d identity-service frontend` | Pass |
| `GET http://localhost:5048/health` | HTTP 200 |
| `GET http://localhost:5227` | HTTP 200 |
| Login Gateway `admin / Admin@123` | Pass |

### Ghi chú

- Lần đầu Docker build frontend sau khi thêm `wasm-tools` bị lỗi `unable to find python in $PATH`; đã fix bằng `python3` trong `Frontend/Dockerfile`.
- Sau khi có `wasm-tools`, Docker publish frontend đã chạy native wasm optimization thay vì publish không tối ưu.
- Chưa gửi email thật qua SMTP vì cần credential/app password hợp lệ và có thể phát sinh email ra ngoài.
