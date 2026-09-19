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

## TC-DOCKER-004 — Khôi phục stack Docker sau khi chuyển Hyper-V sang WSL2

| Mục | Nội dung |
|---|---|
| Ngày chạy | 17/09/2026 |
| Phạm vi | Docker Desktop WSL2 + Docker Compose full stack |
| Mục tiêu | Build lại image và deploy lại hệ thống sau khi Docker mất toàn bộ image/container |
| Kết quả | Pass |

### Điều kiện ban đầu

- Docker Desktop đang chạy bằng WSL2.
- `docker info` ghi nhận Docker root mới trên `/var/lib/docker`.
- `docker info` ghi nhận `Images: 0`.
- `docker compose ps` ban đầu không có container đang chạy.

### Các bước đã chạy

1. Kiểm tra Docker:

```powershell
docker --version
docker compose version
docker info --format "{{json .}}"
docker images
```

2. Build lại image:

```powershell
docker compose build
```

3. Do OCR build bị kéo dài ở bước `pip install`, dừng build tổng và build lại riêng:

```powershell
docker compose build ocr-service
```

4. Chạy stack:

```powershell
docker compose up -d
```

5. Kiểm tra health/login:

```powershell
docker compose ps
Invoke-WebRequest http://localhost:5000/health -UseBasicParsing
Invoke-WebRequest http://localhost:5000/ -UseBasicParsing
Invoke-WebRequest http://localhost:5048/health -UseBasicParsing
Invoke-WebRequest http://localhost:5051/api/ocr/health -UseBasicParsing
Invoke-WebRequest http://localhost:5227/ -UseBasicParsing
POST http://localhost:5000/api/auth/login
```

6. Kiểm tra hạ tầng:

```powershell
docker compose exec -T kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list
docker run --rm --network digitalsign_ocrproject_default --entrypoint /bin/sh quay.io/minio/mc:latest -c "mc alias set local http://minio:9000 minioadmin minioadmin >/dev/null && mc ls local"
```

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Custom images `hau/*` | Đã build lại đủ 6 image |
| `docker compose up -d` | Pass |
| `docker compose ps` | Các container chính `Up`; `postgres` và `identity-service` healthy |
| `GET http://localhost:5000/health` | HTTP 200 |
| `GET http://localhost:5000/` | HTTP 200 |
| `GET http://localhost:5048/health` | HTTP 200 |
| `GET http://localhost:5051/api/ocr/health` | HTTP 200 |
| `GET http://localhost:5227/` | HTTP 200 |
| Login Gateway `admin / Admin@123` | Pass, có access token, role `Admin` |
| MinIO | Có bucket `documents` |
| Kafka | Có topic `document.uploaded` |

### Ghi chú

- Đã tạo hướng dẫn phục hồi: `HUONG_DAN_KHOI_PHUC_DOCKER_WSL2.md`.
- Đã bổ sung link hướng dẫn này vào `TRIEN_KHAI_DOCKER.md`.
- Vì Docker data mới, volume hiện tại là dữ liệu mới rỗng. Nếu cần dữ liệu thật, restore backup PostgreSQL/MinIO/`hau_sign_certs`.
- Docker frontend publish lúc đó có warning `Users._formDepartmentId` chưa được gán; warning không chặn deploy và đã được xử lý ở `TC-FE-OCR-006`.

## TC-OCR-AUTH-005 — OCRService dùng service-token để cập nhật DocumentService

| Mục | Nội dung |
|---|---|
| Ngày chạy | 17/09/2026 |
| Phạm vi | DocumentService + OCRService + Docker internal network |
| Mục tiêu | Xác nhận OCRService có thể PATCH kết quả OCR về DocumentService khi Kafka event không có JWT người dùng |
| Kết quả | Pass |

### Điều kiện trước test

- Docker stack đang chạy.
- `document-service` và `ocr-service` đã được build/recreate từ code mới.
- `document-service` có `ServiceAuth__OcrServiceToken=hau-dev-ocr-service-token`.
- `ocr-service` có `SERVICE_TOKEN=hau-dev-ocr-service-token`.
- Có seed user `admin / Admin@123`.

### Các bước đã chạy

1. Build code:

```powershell
dotnet build .\HAU_DigitalSign_OCR.slnx
python -m compileall OCRService\app
```

2. Chạy test tự động:

```powershell
dotnet test .\HAU_DigitalSign_OCR.slnx --no-build
```

3. Build/redeploy Docker:

```powershell
docker compose build document-service ocr-service
docker compose up -d document-service ocr-service api-gateway
```

4. Kiểm tra OCR health và biến môi trường trong container:

```powershell
Invoke-WebRequest http://localhost:5051/api/ocr/health -UseBasicParsing
docker compose exec -T ocr-service python -c "import os; print(bool(os.getenv('SERVICE_TOKEN')))"
```

5. Login admin qua Gateway.
6. Tạo document test qua Gateway.
7. Gọi trực tiếp DocumentService:

```text
PATCH http://localhost:5049/api/documents/{docId}/ocr
Header: X-Service-Token: hau-dev-ocr-service-token
```

8. Gọi lại thiếu JWT/service-token để xác nhận bị chặn.
9. Từ container OCR, gọi `app.services.document_service.update_ocr_result(...)` tới URL nội bộ `http://document-service:8080` với `SERVICE_TOKEN`.
10. Verify document qua Gateway.

### Dữ liệu/kết quả chính

| Trường | Giá trị |
|---|---|
| `DocId` | `d3ee8215-3759-4525-9b2a-8e7bc0c93d4d` |
| PATCH bằng `X-Service-Token` | `success = true` |
| PATCH thiếu token | HTTP 401 |
| OCR container gọi DocumentService nội bộ | `True` |
| `VerifiedDocNumber` sau bước gọi OCR container | `OCR-SVC-005-CONTAINER` |
| `VerifiedTitle` sau bước gọi OCR container | `TC-OCR-SVC-005 OCR container service token` |
| Smoke test sau recreate DocumentService | PATCH bằng service-token pass; thiếu token HTTP 401 |

### Build/test liên quan

| Lệnh | Kết quả |
|---|---|
| `dotnet build .\HAU_DigitalSign_OCR.slnx` | Pass; còn warning cũ `Users._formDepartmentId`, đã xử lý ở `TC-FE-OCR-006` |
| `python -m compileall OCRService\app` | Pass |
| `dotnet test .\HAU_DigitalSign_OCR.slnx --no-build` | Pass 29/29 |
| `docker compose build document-service ocr-service` | Pass |
| `docker compose up -d document-service ocr-service api-gateway` | Pass |
| `GET http://localhost:5051/api/ocr/health` | HTTP 200 |

### Ghi chú

- Test này xác nhận cơ chế auth service-to-service và đường gọi từ OCR container sang DocumentService.
- Chưa chạy full OCR bằng PaddleOCR qua Kafka upload thật trong test này; phần đó có thể test riêng khi cần kiểm tra chất lượng bóc tách OCR.

## TC-FE-OCR-006 — Frontend hiển thị kết quả OCR từ `OcrDataRaw`

| Mục | Nội dung |
|---|---|
| Ngày chạy | 17/09/2026 |
| Phạm vi | Frontend Blazor + DocumentService API + Docker frontend |
| Mục tiêu | Xác nhận frontend có route riêng xem kết quả OCR và model frontend đọc đúng field OCR thực tế backend trả về |
| Kết quả | Pass |

### Điều kiện trước test

- Docker stack đang chạy.
- `frontend` đã được build/recreate từ code mới.
- DocumentService có endpoint `PATCH /api/documents/{id}/ocr` hoạt động với `X-Service-Token`.
- Có seed user `admin / Admin@123`.

### Các thay đổi chính đã test

- Thêm route frontend `/documents/{id}/ocr`.
- `DocumentDto` frontend đọc đúng `DocNumber`, `DocTypeName`, `MinioPath`, `OcrDataRaw`, `Processes`.
- Giữ alias tương thích cho code UI cũ: `DocumentNumber`, `DocumentTypeName`, `OcrText`, `ProcessHistory`.
- `DocumentService.RunOcrAsync` frontend gọi thật `POST api/ocr/process`.
- Trang chi tiết công văn có nút “Kết quả OCR”.
- Xử lý warning `_formDepartmentId` trong `Users.razor`.

### Các bước đã chạy

1. Build code:

```powershell
dotnet build .\HAU_DigitalSign_OCR.slnx
```

2. Chạy test tự động:

```powershell
dotnet test .\HAU_DigitalSign_OCR.slnx --no-build
```

3. Build/redeploy frontend Docker:

```powershell
docker compose build frontend
docker compose up -d frontend
```

4. Kiểm tra frontend:

```powershell
Invoke-WebRequest http://localhost:5227 -UseBasicParsing
docker compose ps frontend api-gateway document-service ocr-service
```

5. Login admin qua Gateway.
6. Tạo document test qua Gateway.
7. PATCH OCR test bằng service-token với JSON OCR mẫu gồm:
   - `pages[0].lines`
   - `extracted.doc_number`
   - `extracted.issued_date`
   - `extracted.title`
   - `extracted.issuing_org`
8. Verify document qua Gateway.
9. Gọi frontend route:

```text
GET http://localhost:5227/documents/{docId}/ocr
```

### Dữ liệu/kết quả chính

| Trường | Giá trị |
|---|---|
| `DocId` | `1273624e-2816-4bef-adf7-74fc636f2241` |
| PATCH OCR test | `success = true` |
| `VerifiedDocNumber` | `OCR-FE-006-20260917222721` |
| `HasOcrDataRaw` | `true` |
| `ProcessCount` | `2` |
| Frontend route `/documents/{docId}/ocr` | HTTP 200 |

### Build/test liên quan

| Lệnh | Kết quả |
|---|---|
| `dotnet build .\HAU_DigitalSign_OCR.slnx` | Pass 0 warning/0 error |
| `dotnet test .\HAU_DigitalSign_OCR.slnx --no-build` | Pass 29/29 |
| `docker compose build frontend` | Pass |
| `docker compose up -d frontend` | Pass |
| `GET http://localhost:5227` | HTTP 200 |
| `docker compose ps frontend api-gateway document-service ocr-service` | Các container liên quan `Up` |

### Ghi chú

- Test này xác nhận màn hình frontend có thể nhận và hiển thị dữ liệu OCR đã được lưu trong DocumentService.
- Chưa chạy full PaddleOCR trên file PDF thật trong test này; phần đó nên tách thành test chất lượng OCR riêng.

## TC-AUTH-TOKEN-007 — Persist refresh token, rotate token và blacklist logout

| Mục | Nội dung |
|---|---|
| Ngày chạy | 19/09/2026 |
| Phạm vi | IdentityService AuthService + AuthController integration test |
| Mục tiêu | Xác nhận refresh token được lưu DB dạng hash, refresh token được rotate, token cũ không reuse được, logout revoke refresh token và blacklist access token |
| Kết quả | Pass |

### Thay đổi chính đã test

- Thêm bảng/entity `RefreshTokens`.
- Thêm bảng/entity `RevokedAccessTokens`.
- Login lưu hash refresh token vào DB.
- Refresh token kiểm tra token active trong DB và rotate token.
- Reuse refresh token cũ sau khi rotate trả 401.
- Logout revoke toàn bộ refresh token active của user.
- Logout blacklist access token theo `jti`.
- `validate-token` trả `isValid=false` với access token đã logout.
- Protected endpoint trong IdentityService trả 401 nếu dùng lại access token đã logout.

### Các bước đã chạy

1. Build code:

```powershell
dotnet build .\HAU_DigitalSign_OCR.slnx
```

2. Chạy test IdentityService:

```powershell
dotnet test .\IdentityService\tests\IdentityService.Tests\IdentityService.Tests.csproj
```

3. Chạy toàn bộ test solution:

```powershell
dotnet test .\HAU_DigitalSign_OCR.slnx
```

4. Docker build/redeploy:

```powershell
docker compose build identity-service
docker compose up -d identity-service api-gateway
docker compose ps identity-service api-gateway postgres
```

5. Smoke test qua Gateway:
   - Login `admin / Admin@123`.
   - Refresh token bằng access/refresh token vừa login.
   - Gọi lại refresh bằng refresh token cũ.
   - Logout bằng access token mới.
   - Validate token sau logout.
   - Gọi `/api/users` bằng token đã logout.
   - Gọi refresh token sau logout.

6. Kiểm tra DB:

```powershell
SELECT to_regclass('"RefreshTokens"'), to_regclass('"RevokedAccessTokens"');
SELECT COUNT(*) FROM "RefreshTokens";
SELECT COUNT(*) FROM "RevokedAccessTokens";
```

### Kết quả build/test

| Lệnh | Kết quả |
|---|---|
| `dotnet build .\HAU_DigitalSign_OCR.slnx` | Pass 0 warning/0 error |
| `dotnet test .\IdentityService\tests\IdentityService.Tests\IdentityService.Tests.csproj` | Pass 29/29 |
| `dotnet test .\HAU_DigitalSign_OCR.slnx` | Pass 31/31 |
| Start Docker Desktop + `docker info` | Pass, Docker server `29.5.3` |
| `docker compose build identity-service` | Pass |
| `docker compose up -d identity-service api-gateway` | Pass |
| `docker compose ps identity-service api-gateway postgres` | `identity-service` và `postgres` healthy; `api-gateway` up |

### Kết quả smoke test Docker/Gateway

| Kiểm tra | Kết quả |
|---|---|
| Login admin | OK |
| Refresh token | OK, refresh token mới khác token cũ |
| Reuse refresh token cũ | HTTP 401 |
| Logout | `Đăng xuất thành công` |
| `validate-token` sau logout | `isValid = false` |
| `GET /api/users` bằng token đã logout | HTTP 401 |
| Refresh token sau logout | HTTP 401 |
| Bảng `RefreshTokens` | Tồn tại, có 2 dòng sau test |
| Bảng `RevokedAccessTokens` | Tồn tại, có 1 dòng sau test |

### Integration test đã thêm

- `RefreshToken_WithStoredRefreshToken_ShouldRotateAndRejectOldRefreshToken`
- `Logout_ShouldBlacklistAccessTokenAndRevokeRefreshTokens`

### Ghi chú

- Lúc đầu Docker daemon chưa chạy; đã start Docker Desktop và chạy lại bước Docker thành công.
- Sau `TC-GW-AUTH-008`, ApiGateway đã kiểm tra blacklist qua IdentityService nên token đã logout bị chặn trên route Document/Sign/OCR qua Gateway.

## TC-GW-AUTH-008 — ApiGateway chặn token đã logout trên route downstream

| Mục | Nội dung |
|---|---|
| Ngày chạy | 19/09/2026 |
| Phạm vi | ApiGateway + IdentityService + DocumentService qua Docker/Gateway |
| Mục tiêu | Xác nhận ApiGateway không chỉ validate JWT local mà còn gọi IdentityService `validate-token` để chặn access token đã logout trước khi proxy sang downstream service |
| Kết quả | Pass |

### Thay đổi chính đã test

- `ApiGateway/Program.cs` gọi IdentityService `/api/auth/validate-token` trong JWT `OnTokenValidated`.
- `ApiGateway/appsettings.json` có `AuthValidation:ValidateTokenUrl` và `AuthValidation:TimeoutSeconds`.
- `docker-compose.yml` cấu hình Gateway gọi IdentityService nội bộ bằng URL `http://identity-service:8080/api/auth/validate-token`.

### Các bước đã chạy

1. Build code:

```powershell
dotnet build .\HAU_DigitalSign_OCR.slnx
```

2. Chạy test tự động:

```powershell
dotnet test .\HAU_DigitalSign_OCR.slnx
```

3. Kiểm tra Docker Compose config:

```powershell
docker compose config --quiet
```

4. Build/redeploy Gateway:

```powershell
docker compose build api-gateway
docker compose up -d api-gateway
```

5. Smoke test qua Gateway:
   - `GET /health`.
   - Login `admin / Admin@123`.
   - Gọi `GET /api/documents` trước logout bằng token hợp lệ.
   - Logout bằng `POST /api/auth/logout`.
   - Gọi `POST /api/auth/validate-token` với access token vừa logout.
   - Gọi lại `GET /api/documents` bằng token đã logout.

### Kết quả build/test

| Lệnh | Kết quả |
|---|---|
| `dotnet build .\HAU_DigitalSign_OCR.slnx` | Pass 0 warning/0 error |
| `dotnet test .\HAU_DigitalSign_OCR.slnx` | Pass 31/31 |
| `docker compose config --quiet` | Pass |
| `docker compose build api-gateway` | Pass |
| `docker compose up -d api-gateway` | Pass |

### Kết quả smoke test

| Kiểm tra | Kết quả |
|---|---|
| Gateway `/health` | HTTP 200 |
| Login admin | OK |
| `GET /api/documents` trước logout | HTTP 200 |
| Logout | `Đăng xuất thành công` |
| `validate-token` sau logout | `isValid = false` |
| `GET /api/documents` sau logout | HTTP 401 |
| Body lỗi sau logout | `{"success":false,"message":"Bạn chưa đăng nhập hoặc token không hợp lệ."}` |

### Ghi chú

- Lần đầu `docker compose build api-gateway` lỗi YAML do biến URL mới chưa quote và block `environment` bị lệch indent; đã sửa và xác nhận bằng `docker compose config --quiet`.
- Sau `TC-GW-AUTH-009`, Gateway có thể fallback sang JWT local khi IdentityService validate-token tạm lỗi nếu `AuthValidation:FailOpenOnValidationError=true`.

## TC-GW-AUTH-009 — Gateway fallback sang JWT local khi IdentityService tạm lỗi

| Mục | Nội dung |
|---|---|
| Ngày chạy | 19/09/2026 |
| Phạm vi | ApiGateway + IdentityService + DocumentService qua Docker/Gateway |
| Mục tiêu | Xác nhận Gateway không làm gián đoạn route downstream khi IdentityService validate-token tạm lỗi/timeout, trong khi vẫn chặn token logout khi IdentityService hoạt động |
| Kết quả | Pass |

### Thay đổi chính đã test

- Thêm `AuthValidation:FailOpenOnValidationError`.
- Docker cấu hình `AuthValidation__FailOpenOnValidationError=true`.
- Khi IdentityService validate-token lỗi/timeout, Gateway log warning và fallback sang kết quả JWT local.
- Khi IdentityService phản hồi `isValid=false`, Gateway vẫn chặn 401.

### Các bước đã chạy

1. Build code:

```powershell
dotnet build .\HAU_DigitalSign_OCR.slnx
```

2. Chạy test tự động:

```powershell
dotnet test .\HAU_DigitalSign_OCR.slnx
```

3. Kiểm tra Docker Compose config và build/redeploy Gateway:

```powershell
docker compose config --quiet
docker compose build api-gateway
docker compose up -d api-gateway
```

4. Kiểm tra blacklist vẫn hoạt động:
   - Login admin.
   - `GET /api/documents` trước logout: 200.
   - Logout.
   - `validate-token`: `isValid=false`.
   - `GET /api/documents` sau logout: 401.

5. Kiểm tra fallback khi IdentityService tạm dừng:
   - Login admin lấy token mới.
   - `GET /api/documents` trước khi dừng IdentityService: 200.
   - `docker compose stop identity-service`.
   - `GET /api/documents` với token hợp lệ local trong lúc IdentityService dừng: 200.
   - `docker compose up -d identity-service`.
   - Health IdentityService trở lại 200.

### Kết quả build/test

| Lệnh | Kết quả |
|---|---|
| `dotnet build .\HAU_DigitalSign_OCR.slnx` | Pass 0 warning/0 error |
| `dotnet test .\HAU_DigitalSign_OCR.slnx` | Pass 31/31 |
| `docker compose config --quiet` | Pass |
| `docker compose build api-gateway` | Pass |
| `docker compose up -d api-gateway` | Pass |

### Kết quả smoke test

| Kiểm tra | Kết quả |
|---|---|
| Blacklist: `/api/documents` trước logout | HTTP 200 |
| Blacklist: `validate-token` sau logout | `isValid=false` |
| Blacklist: `/api/documents` sau logout | HTTP 401 |
| Fallback: `/api/documents` trước khi dừng IdentityService | HTTP 200 |
| Fallback: `/api/documents` khi IdentityService dừng | HTTP 200 |
| IdentityService sau khi restart | Health HTTP 200 |

### Ghi chú

- `FailOpenOnValidationError=true` ưu tiên tính sẵn sàng: downstream route vẫn chạy nếu JWT hợp lệ local và IdentityService tạm lỗi.
- Đánh đổi: trong thời gian IdentityService tạm lỗi, token đã logout có thể chưa bị Gateway chặn cho tới khi IdentityService phục hồi hoặc token hết hạn.
- Có thể đổi sang `false` ở production nếu muốn ưu tiên bảo mật tuyệt đối.
