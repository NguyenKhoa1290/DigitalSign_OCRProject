# Test Cases

> Ghi lại test case đã chạy theo quy trình: viết code → build code → Docker → test case → ghi test case → báo cáo.

> Kết quả trong từng mục thuộc thời điểm chạy, không phải xác nhận runtime hiện tại. Các viewport 390px và ngưỡng cao 720px là thông số lịch sử; source hiện dùng tối thiểu 450×500 và breakpoint modal trong `TC-FE-MODAL-BREAKPOINT-037`.

> Bàn giao ngày 05/10/2026: kiểm thử thực tế/nghiệm thu do người dùng tự thực hiện, trạng thái **Chờ người dùng kiểm thử và xác nhận**. Kết quả tự động/local/mô phỏng bên dưới không thay thế nghiệm thu trên email, PDF/scan và dữ liệu nghiệp vụ thực tế. Danh sách bàn giao tại mục 7.3 của `2_da_lam.md`; chưa có kết quả nghiệm thu mới do người dùng cung cấp.

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

## TC-OCR-E2E-010 — Upload PDF thật qua Kafka/PaddleOCR và cập nhật OCR về DocumentService

| Mục | Nội dung |
|---|---|
| Ngày chạy | 20/09/2026 |
| Phạm vi | DocumentService + Kafka + MinIO + OCRService + ApiGateway trong Docker |
| Mục tiêu | Xác nhận upload PDF thật qua Gateway kích hoạt Kafka event, OCRService xử lý bằng PaddleOCR và tự PATCH kết quả OCR về DocumentService |
| Kết quả | Pass |

### Thay đổi chính đã test

- `OCRService/app/services/kafka_consumer.py` có retry loop khi Kafka chưa sẵn sàng/lỗi kết nối.
- `OCRService/app/main.py` dùng event loop FastAPI lifespan để chạy coroutine OCR từ Kafka thread.
- `OCRService/requirements.txt` pin thêm OpenCV packages để Docker build OCRService ổn định hơn.

### Các bước đã chạy

1. Kiểm tra code Python:

```powershell
python -m compileall .\OCRService\app
```

2. Build/redeploy OCRService:

```powershell
docker compose build ocr-service
docker compose up -d ocr-service
```

3. Kiểm tra health và hạ tầng:

```powershell
Invoke-WebRequest http://localhost:5051/api/ocr/health -UseBasicParsing
docker compose exec -T kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list
```

4. Chạy luồng qua Gateway:
   - Login `admin / Admin@123`.
   - `GET /api/documents/types` để lấy `DocTypeId`.
   - Tạo document test.
   - Tạo PDF test có nội dung lớn/rõ:
     - `TRUONG DAI HOC KIEN TRUC HA NOI`
     - `So: 123/CV-HAU`
     - `Ngay: 20/09/2026`
     - `V/v kiem thu OCR tu dong qua Kafka`
   - Upload PDF vào `/api/documents/{docId}/upload`.
   - Poll `GET /api/documents/{docId}` cho tới khi `ocrDataRaw` có dữ liệu.

### Dữ liệu test thực tế

| Trường | Giá trị |
|---|---|
| `docId` | `bba87f60-5a39-43c6-801d-8adcf8fa478c` |
| File test | `tc-ocr-e2e-010-20260920103916.pdf` |
| MinIO path | `documents/63df61e8-04bf-47af-bdc4-069f3470d4a3.pdf` |
| Kafka topic | `document.uploaded` |
| Kafka offset | `0` |

### Kết quả build/test

| Lệnh/kiểm tra | Kết quả |
|---|---|
| `python -m compileall .\OCRService\app` | Pass |
| `docker compose build ocr-service` | Pass |
| `docker compose up -d ocr-service` | Pass |
| OCR health | HTTP 200 |
| Kafka topic `document.uploaded` | Tồn tại |
| Upload PDF qua Gateway | HTTP 200 |
| DocumentService publish Kafka | OK, partition `0`, offset `0` |
| OCRService/PaddleOCR xử lý và PATCH DocumentService | Pass, có `UpdateOCR` |

### Kết quả OCR sau khi xử lý

| Trường | Giá trị |
|---|---|
| `docNumber` | `123/CV-HAU` |
| `issuedDate` | `2026-09-20` |
| `title` | `kiem thu OCR tu dong qua Kafka` |
| `ocrDataRaw` | Có JSON gồm `pages`, `lines`, `extracted` |
| `DocumentProcess` | Có action `UpdateOCR` |
| Actor cập nhật OCR | `00000000-0000-0000-0000-000000000051` |

### Ghi chú

- Test này xác nhận full luồng runtime, không chỉ test service-token/PATCH mẫu.
- OCR nhận diện tốt với PDF test chữ rõ; cần thêm bộ PDF/scan thực tế để đánh giá chất lượng OCR trong điều kiện tài liệu thật.

## TC-SEC-ENV-011 — Docker Compose dùng `.env`/`.env.example` cho secret triển khai

| Mục | Nội dung |
|---|---|
| Ngày chạy | 20/09/2026 |
| Phạm vi | Docker Compose + cấu hình triển khai + Gateway/Identity/OCR smoke test |
| Mục tiêu | Xác nhận các secret chính có thể override qua `.env` mà vẫn giữ default dev để local chạy được |
| Kết quả | Pass |

### Thay đổi chính đã test

- Thêm `.env.example` ở root project.
- Thêm `.gitignore` để bỏ qua `.env`/`.env.*` nhưng vẫn track `.env.example`.
- Cập nhật `.dockerignore` để không đưa `.env` thật vào Docker build context.
- Cập nhật `docker-compose.yml` dùng `${VAR:-default_dev}` cho:
  - PostgreSQL user/password/database.
  - MinIO root user/password.
  - JWT key/issuer/audience/token expiry.
  - OCR service-token.
- Cập nhật `TRIEN_KHAI_DOCKER.md` và `OCRService/.env.example`.

### Các bước đã chạy

1. Kiểm tra Compose với default dev, không cần `.env`:

```powershell
docker compose config --quiet
```

2. Kiểm tra Compose đọc được `.env.example`:

```powershell
docker compose --env-file .env.example config --quiet
```

3. Redeploy stack hiện tại:

```powershell
docker compose up -d
```

4. Kiểm tra trạng thái container:

```powershell
docker compose ps
```

5. Smoke test:
   - Gateway health.
   - Identity health.
   - OCR health.
   - Login qua Gateway bằng `admin / Admin@123`.

### Kết quả build/test

| Lệnh/kiểm tra | Kết quả |
|---|---|
| `docker compose config --quiet` | Pass |
| `docker compose --env-file .env.example config --quiet` | Pass |
| `docker compose up -d` | Pass |
| `docker compose ps` | Các container chính `Up`, PostgreSQL và Identity healthy |
| Gateway health | `Healthy` |
| Identity health | `Healthy` |
| OCR health | `status = ok` |
| Login qua Gateway | Pass, nhận access token |

### Ghi chú

- `.env.example` dùng placeholder `change-me-*` cho triển khai thật.
- Nếu không tạo `.env`, Compose vẫn dùng default dev trong `docker-compose.yml` để không làm gián đoạn môi trường local hiện tại.
- Các file `appsettings*.json` vẫn có giá trị dev/local; khi chạy Docker, environment variables từ Compose sẽ override các giá trị này.

## TC-SIGN-ROLE-012 — Ký số bằng role thật `Manager` và `BoardOfDirectors`

| Mục | Nội dung |
|---|---|
| Ngày chạy | 20/09/2026 |
| Phạm vi | IdentityService + DocumentService + SignService + MinIO + ApiGateway trong Docker |
| Mục tiêu | Xác nhận ký nháy/ký pháp nhân hoạt động với user role nghiệp vụ thật và chặn sai quyền đúng kỳ vọng |
| Kết quả | Pass |

### Dữ liệu test thực tế

| Trường | Giá trị |
|---|---|
| Manager user | `tc_manager_20260920131615` |
| Manager id | `5119eb6a-e937-4aa0-9d18-49cd08564042` |
| Board user | `tc_board_20260920131615` |
| Board id | `a4496098-df3a-4a36-9bb9-68cc2d46a38d` |
| Document id | `8a5d61d0-23bf-4e27-a1cb-8cf275c02e73` |
| MinIO path | `documents/a8330e0c-52ac-4a04-8f9a-6c20b5aff0cf.pdf` |

### Các bước đã chạy

1. Login admin qua Gateway.
2. Lấy role `Manager` và `BoardOfDirectors`.
3. Tạo 2 user test:
   - User `Manager`.
   - User `BoardOfDirectors`.
4. Login bằng từng user để lấy token role thật.
5. Admin cấp certificate cho cả 2 user.
6. Tạo document test và upload PDF qua `/api/documents/{docId}/upload`.
7. Submit document sang `PendingDeptReview`.
8. Negative role test:
   - Board gọi `POST /api/signatures/personal-sign`.
   - Manager gọi `POST /api/signatures/legal-seal`.
9. Positive role test:
   - Manager gọi `POST /api/signatures/personal-sign`.
   - Manager gọi `POST /api/documents/{docId}/dept-sign`.
   - Board gọi `POST /api/signatures/legal-seal`.
   - Board gọi `POST /api/documents/{docId}/director-sign`.
10. Kiểm tra danh sách chữ ký và verify PDF:
   - `GET /api/signatures/document/{docId}`.
   - `GET /api/signatures/document/{docId}/verify`.
11. Kiểm tra document cuối cùng.

### Kết quả kiểm thử

| Kiểm tra | Kết quả |
|---|---|
| Board gọi `personal-sign` | HTTP 403 |
| Manager gọi `legal-seal` | HTTP 403 |
| Certificate Manager | `isValid = true` |
| Certificate Board | `isValid = true` |
| Manager ký nháy | Pass, `PersonalSignature` |
| Board ký pháp nhân | Pass, `LegalSeal` |
| Final document status | `DirectorSigned` |
| Số chữ ký lưu trong SignService | `2` |
| Verify PDF | `isValid = true`, `signatureCount = 2` |
| Process actions | `Submit, Submit, UpdateOCR, DeptSign, DirectorSign` |

### Ghi chú

- Test chạy qua API Gateway bằng JWT của user role thật, không dùng Admin để ký thay.
- Đây là kiểm thử API/role end-to-end; nếu cần đánh giá trải nghiệm người dùng, vẫn nên click thử luồng tương ứng trên trình duyệt.
- Upload PDF kích hoạt OCR Kafka nên lịch sử document có thêm action `UpdateOCR`; điều này không ảnh hưởng luồng ký.

## TC-FE-SIGN-UI-013 — Kiểm thử UI ký số trực tiếp trên frontend

| Mục | Nội dung |
|---|---|
| Ngày chạy | 20/09/2026 |
| Phạm vi | Frontend Blazor WASM + ApiGateway + IdentityService + DocumentService + SignService + MinIO + Kafka/OCR |
| Mục tiêu | Xác nhận thao tác click UI ký số hoạt động với user role thật `Manager` và `BoardOfDirectors` |
| Kết quả | Pass |

### Dữ liệu test thực tế

| Trường | Giá trị |
|---|---|
| Manager user | `ui_manager_20260920062601` |
| Board user | `ui_board_20260920062601` |
| Document id | `6fc9e8b8-3a55-4c77-932a-6c19d0b0f57d` |
| MinIO path | `documents/b4f6351b-5400-4dfa-925a-84752db670cd.pdf` |

### Các bước đã chạy

1. Tạo dữ liệu test bằng API Gateway:
   - Tạo user `Manager`.
   - Tạo user `BoardOfDirectors`.
   - Đổi mật khẩu lần đầu cho 2 user để login frontend không bị redirect `/first-login`.
   - Admin cấp certificate cho cả 2 user.
   - Tạo document test, upload PDF và submit sang `PendingDeptReview`.
2. Dùng Playwright headless thao tác frontend `http://localhost:5227`:
   - Login Manager.
   - Vào `/signatures/{docId}`.
   - Bấm `Ký nháy (Manager)` và xác nhận modal ký.
   - Vào `/documents/{docId}`.
   - Bấm workflow `Ký nháy`.
   - Login Board trong browser context riêng.
   - Vào `/signatures/{docId}`.
   - Bấm `Ký số pháp nhân (BGH)` và xác nhận modal ký.
   - Vào `/documents/{docId}`.
   - Bấm workflow `Ký số pháp nhân`.
   - Vào lại `/signatures/{docId}` và bấm `Xác minh chữ ký`.
3. Verify lại bằng API Gateway:
   - `GET /api/documents/{docId}`.
   - `GET /api/signatures/document/{docId}`.
   - `GET /api/signatures/document/{docId}/verify`.

### Kết quả kiểm thử

| Kiểm tra | Kết quả |
|---|---|
| Manager login frontend | Pass |
| Manager bấm ký nháy trên UI | Pass |
| Manager bấm workflow ký nháy trên UI | Pass |
| Board login frontend | Pass |
| Board bấm ký pháp nhân trên UI | Pass |
| Board bấm workflow ký pháp nhân trên UI | Pass |
| UI verify text | `✓ Tất cả 2 chữ ký đều hợp lệ` |
| Final document status | `DirectorSigned` |
| Số chữ ký | `2` |
| Loại chữ ký | `PersonalSignature`, `LegalSeal` |
| Verify API | `isValid = true`, `signatureCount = 2` |
| Process actions | `Submit, Submit, UpdateOCR, DeptSign, DirectorSign` |

### Ghi chú

- Test này xác nhận thao tác click UI ký số chính đã pass.
- Trang ký số `/signatures/{docId}` thực hiện ký PDF qua SignService; trang chi tiết `/documents/{docId}` thực hiện workflow DocumentService. Vì vậy test UI chạy cả hai trang để hoàn tất luồng nghiệp vụ.
- Upload PDF kích hoạt OCR Kafka nên lịch sử document có thêm action `UpdateOCR`; không ảnh hưởng kết quả ký số.

## TC-OCR-SCAN-014 — OCR PDF dạng scan/image-based qua Kafka

| Mục | Nội dung |
|---|---|
| Ngày chạy | 20/09/2026 |
| Phạm vi | DocumentService + Kafka + MinIO + OCRService/PaddleOCR + ApiGateway |
| Mục tiêu | Xác nhận OCRService xử lý được PDF chứa ảnh scan, không chỉ PDF text rõ |
| Kết quả | Pass |

### Dữ liệu test thực tế

| Trường | Giá trị |
|---|---|
| Document id | `dda7d33f-6c93-4e12-b42f-37c72da1fad5` |
| File test | `tc-ocr-scan-014-20260920133200.pdf` |
| File size | `243107` bytes |
| MinIO path | `documents/8e958a57-0797-4397-8bbb-2d30e685a69c.pdf` |

### Nội dung PDF scan giả lập

PDF được tạo tạm ngoài repo bằng Python/Pillow:

- Ảnh A4 300 DPI.
- Text được vẽ lên ảnh, sau đó lưu ảnh thành PDF.
- Có nhiễu nhẹ, border scan và xoay nhẹ 0.7 độ.

Nội dung:

```text
TRUONG DAI HOC KIEN TRUC HA NOI
So: 456/QD-HAU
Ngay: 20/09/2026
V/v kiem thu OCR tu PDF scan
TC-OCR-SCAN-014 20260920133200
```

### Các bước đã chạy

1. Tạo PDF scan giả lập bằng Python/Pillow trong thư mục `%TEMP%`.
2. Login admin qua Gateway.
3. Tạo document test.
4. Upload PDF qua `POST /api/documents/{docId}/upload`.
5. DocumentService publish Kafka event `document.uploaded`.
6. Poll `GET /api/documents/{docId}` cho tới khi có `ocrDataRaw`.
7. Kiểm tra fields bóc tách và lịch sử xử lý.

### Kết quả kiểm thử

| Kiểm tra | Kết quả |
|---|---|
| Upload PDF scan qua Gateway | Pass |
| OCR tự động qua Kafka | Pass, có `ocrDataRaw` sau poll đầu khoảng 10 giây |
| Số dòng OCR | `5` |
| `docNumber` | `456/QD-HAU` |
| `issuedDate` | `2026-09-20` |
| `title` | `kiem thu OCR tu PDF scan` |
| Process actions | `Submit, UpdateOCR` |

### Ghi chú

- Test này dùng scan giả lập bằng ảnh PDF, chưa phải file scan thật của nhà trường.
- Khi có dữ liệu thật, nên bổ sung thêm bộ test gồm scan mờ, lệch, nhiều trang, có dấu đỏ/chữ ký để đánh giá chất lượng OCR thực tế.

## TC-AUTH-MAILPIT-015 — Forgot/reset password qua Mailpit SMTP local

| Mục | Nội dung |
|---|---|
| Ngày chạy | 20/09/2026 |
| Phạm vi | IdentityService + ApiGateway + Frontend + Mailpit Docker |
| Mục tiêu | Xác nhận forgot/reset password gửi OTP qua SMTP local, reset được mật khẩu và login lại được |
| Kết quả | Pass |

### Thay đổi chính đã test

- `EmailService` hỗ trợ cấu hình:
  - `EmailSettings:SecureSocketOptions`.
  - `EmailSettings:RequireAuth`.
  - `EmailSettings:FromEmail`.
- `docker-compose.yml` có service `mailpit`.
- IdentityService Docker dev trỏ SMTP về Mailpit:
  - Host: `mailpit`.
  - Port: `1025`.
  - `RequireAuth=false`.
  - `SecureSocketOptions=None`.
- Mailpit Web UI/API publish ở `http://localhost:8025`.

### Kết quả build/Docker

| Lệnh/kiểm tra | Kết quả |
|---|---|
| `dotnet build .\IdentityService\src\IdentityService.API\IdentityService.API.csproj` | Pass 0 warning/0 error |
| `docker compose config --quiet` | Pass |
| `docker compose build identity-service` | Pass |
| `docker compose up -d mailpit identity-service api-gateway` | Pass |
| Identity health | `Healthy` |
| Mailpit Web UI/API | HTTP 200 |
| `dotnet test .\IdentityService\tests\IdentityService.Tests\IdentityService.Tests.csproj --no-build` | Pass 29/29 |

### Backend/API test

| Trường | Giá trị |
|---|---|
| User email | `mailpit_user_20260920133942@hau.test` |
| Mailpit message id | `7Sc5dGCvQV96DwoqBFrOv0` |
| OTP length | `6` |
| Reset result | `Đặt lại mật khẩu thành công. Vui lòng đăng nhập lại.` |
| Login user sau reset | `mailpit_user_20260920133942` |
| `mustChangePassword` sau reset | `false` |

Các bước:

1. Tạo user test có email.
2. Gọi `POST /api/auth/forgot-password`.
3. Đọc email OTP trong Mailpit API.
4. Gọi `POST /api/auth/reset-password` bằng OTP.
5. Login bằng mật khẩu mới.

### Frontend/UI test

| Trường | Giá trị |
|---|---|
| User | `ui_reset_20260920064037` |
| Email | `ui_reset_20260920064037@hau.test` |
| Mailpit message id | `0P2YGxZv1S0x8UbO4gyKHf` |
| Final URL sau login | `http://localhost:5227/` |
| API login user xác nhận | `ui_reset_20260920064037` |
| `mustChangePassword` | `false` |

Các bước Playwright:

1. Tạo user test bằng API.
2. Mở frontend `/forgot-password`.
3. Nhập email và gửi OTP.
4. Lấy OTP từ Mailpit API.
5. Mở frontend `/reset-password`.
6. Nhập email, OTP, mật khẩu mới.
7. Reset thành công, redirect `/login`.
8. Login frontend bằng mật khẩu mới và vào được trang `/`.

### Ghi chú

- Mailpit chỉ dùng local/dev, không gửi email ra internet.
- Khi deploy production, đổi biến `EMAIL_*` trong `.env` sang SMTP thật và test lại với credential thật.

## TC-DOC-WF-016 — Workflow `DeptSigned` và `submit-director`

| Mục | Nội dung |
|---|---|
| Ngày chạy | 20/09/2026 |
| Phạm vi | DocumentService + ApiGateway + Docker |
| Mục tiêu | Xác nhận trạng thái `DeptSigned` được dùng thật và phải qua bước `submit-director` trước khi BGH ký |
| Kết quả | Pass |

### Dữ liệu test thực tế

| Trường | Giá trị |
|---|---|
| User chạy API | `admin / Admin@123` |
| Document id | `7c186598-4e8d-4343-85a5-1f1a295743dc` |
| Edge document id | `2808ff67-a5ab-4548-959d-e48fb01f0189` |

### Các bước đã chạy

1. Login admin qua Gateway.
2. Lấy document type qua `GET /api/documents/types`.
3. Tạo document mới qua `POST /api/documents`.
4. Gọi `POST /api/documents/{docId}/submit`.
5. Gọi `POST /api/documents/{docId}/dept-sign`.
6. Kiểm tra status sau ký nháy là `DeptSigned`.
7. Gọi `POST /api/documents/{docId}/submit-director`.
8. Kiểm tra status là `PendingDirectorSign`.
9. Gọi `POST /api/documents/{docId}/director-sign`.
10. Kiểm tra status cuối là `DirectorSigned`.
11. Tạo document edge case, đưa tới `DeptSigned`, gọi thẳng `director-sign` và kỳ vọng HTTP 422.

### Kết quả kiểm thử

| Kiểm tra | Kết quả |
|---|---|
| `submit` | `Draft -> PendingDeptReview` |
| `dept-sign` | `PendingDeptReview -> DeptSigned` |
| `submit-director` | `DeptSigned -> PendingDirectorSign` |
| `director-sign` | `PendingDirectorSign -> DirectorSigned` |
| Process actions | `Submit, Submit, DeptSign, SubmitDirector, DirectorSign` |
| Gọi `director-sign` trực tiếp từ `DeptSigned` | HTTP 422 |
| Status sau edge case lỗi | Vẫn là `DeptSigned` |

## TC-FE-WF-017 — UI nút `Trình BGH ký` trên trang chi tiết văn bản

| Mục | Nội dung |
|---|---|
| Ngày chạy | 20/09/2026 |
| Phạm vi | Frontend Blazor WASM + ApiGateway + DocumentService |
| Mục tiêu | Xác nhận nút `Trình BGH ký` ở trạng thái `DeptSigned` gọi đúng API `submit-director` |
| Kết quả | Pass |

### Dữ liệu test thực tế

| Trường | Giá trị |
|---|---|
| Manager user | `ui_wf_manager_20260920135343` |
| Document id | `837da503-9e73-4085-a9fc-ce20a04c3c73` |

### Các bước đã chạy

1. Tạo user role `Manager` qua API Gateway.
2. Đổi mật khẩu lần đầu để login frontend không bị redirect `/first-login`.
3. Tạo document và đưa tới trạng thái `DeptSigned` bằng API.
4. Dùng Playwright headless mở `http://localhost:5227/login`.
5. Login bằng user Manager.
6. Mở `/documents/{docId}`.
7. Bấm nút `Trình BGH ký`.
8. Xác nhận modal.
9. Kiểm tra UI hiển thị trạng thái `Chờ BGH ký`.
10. Kiểm tra lại bằng API.

### Kết quả kiểm thử

| Kiểm tra | Kết quả |
|---|---|
| Login frontend Manager | Pass |
| Nút `Trình BGH ký` hiển thị khi status `DeptSigned` | Pass |
| Bấm nút và xác nhận modal | Pass |
| Status sau UI action | `PendingDirectorSign` |
| Process actions | `Submit, Submit, DeptSign, SubmitDirector` |

## TC-AUTH-GMAIL-018 — Gửi OTP bằng tài khoản Google

| Mục | Nội dung |
|---|---|
| Ngày soạn | 24/09/2026 |
| Phạm vi | IdentityService + ApiGateway + Gmail SMTP |
| Mục tiêu | Xác nhận email OTP được gửi ra internet với người gửi là tài khoản Google đã cấu hình |
| Trạng thái | Hoàn tất bước gửi/nhận email; chưa chạy reset bằng OTP thật và đăng nhập lại |

Kiểm tra kỹ thuật đã đạt: build IdentityService, 29/29 unit test, Docker Compose config, Docker image và health của IdentityService/Gateway.

### Kết quả chạy ngày 24/09/2026

| Kiểm tra | Kết quả |
|---|---|
| `.env` chứa Gmail credential và được Git bỏ qua | Pass |
| Recreate IdentityService/Gateway | Pass |
| Health IdentityService/Gateway | HTTP 200 `Healthy` |
| User test | `gmail_test_20260924221509` |
| `POST /api/auth/forgot-password` | HTTP 200 |
| Log lỗi SMTP | Không có |
| Gmail SMTP chấp nhận gửi | Pass |
| Xác nhận email tại Inbox/Spam | Người nhận đã xác nhận email trong hộp thư, theo Công việc số 17 trong `3_nhat_ky.md` |
| Dùng OTP reset password và login lại | Chưa chạy |

Đồng bộ ngày 05/10/2026 theo nhật ký ngày 24/09/2026; không gửi lại email hoặc chạy lại test Gmail trong lần cập nhật tài liệu này.

### Điều kiện

- Tài khoản Google đã bật xác minh 2 bước.
- Đã tạo App Password riêng cho ứng dụng.
- `.env` dùng `smtp.gmail.com`, cổng `587`, `StartTls`, bật xác thực.
- `EMAIL_FROM_EMAIL` trùng `EMAIL_USERNAME` hoặc là địa chỉ gửi thay đã được Gmail cho phép.

### Các bước kiểm thử

1. Recreate `identity-service` và `api-gateway` để nhận biến môi trường mới.
2. Gọi `POST /api/auth/forgot-password` với email người nhận thật.
3. Xác nhận API không trả lỗi SMTP.
4. Kiểm tra Inbox/Spam của người nhận.
5. Xác nhận trường From đúng tài khoản Google cấu hình.
6. Lấy OTP trong email và gọi `POST /api/auth/reset-password`.
7. Login bằng mật khẩu mới.

### Kết quả mong đợi

- Email tới được người nhận, nội dung OTP đúng mẫu HAU Documents.
- From đúng tài khoản Google cấu hình.
- OTP dùng được đúng một lần và hết hạn theo quy định hiện tại.
- Không có App Password xuất hiện trong log hoặc source control.

## TC-AUTH-EMAIL-VERIFY-019 — Xác minh email khi đăng nhập lần đầu

| Mục | Nội dung |
|---|---|
| Ngày chạy | 24/09/2026 |
| Phạm vi | IdentityService + ApiGateway + Frontend + Mailpit/Gmail |
| Mục tiêu | Chỉ hoàn tất first login sau khi user chứng minh quyền sở hữu email bằng OTP |
| Kết quả | Pass |

### Dữ liệu test

| Trường | Giá trị |
|---|---|
| User | `email_verified_20260924223327` |
| Email test | Email duy nhất trong Mailpit |
| OTP | 6 số, không ghi plain text vào tài liệu |

### Các bước và kết quả

| Kiểm tra | Kết quả |
|---|---|
| Gửi OTP qua `POST /api/auth/send-email-verification` | HTTP 200 |
| Mailpit nhận email xác minh | Pass |
| Forgot password trước khi email được xác minh | Không gửi email |
| First login không gửi OTP | HTTP 400 |
| First login với OTP hợp lệ | HTTP 200 |
| `IsEmailVerified` sau xác minh | `true` |
| Login lại bằng mật khẩu mới | Pass |
| `MustChangePassword` sau xác minh | `false` |
| Forgot password sau khi email được xác minh | Có gửi email |
| Dùng lại OTP đã sử dụng | HTTP 400 |
| Unit test IdentityService | 34/34 pass |
| Build backend/frontend | 0 warning/0 error |
| Docker build IdentityService/Frontend | Pass |
| Health IdentityService/Gateway/Frontend | HTTP 200 |
| Gửi email xác minh bằng Gmail SMTP thật | HTTP 200 |

### Kết luận

- Email không được lưu trong first login nếu thiếu hoặc sai OTP.
- OTP chỉ hợp lệ cho đúng user và đúng email đã yêu cầu.
- OTP hết hạn sau 15 phút, bị vô hiệu hóa khi gửi lại hoặc sau khi dùng thành công.
- Chỉ email có `EmailVerifiedAt` mới được dùng để nhận OTP quên mật khẩu.

## TC-FE-GLASS-020 — Giao diện nhận diện HAU và glassmorphism

| Mục | Nội dung |
|---|---|
| Ngày chạy | 27/09/2026 |
| Phạm vi | Frontend Blazor WASM + Nginx Docker + Chromium |
| Mục tiêu | Xác nhận logo HAU, theme glassmorphism, fallback và responsive hoạt động sau khi đóng image Docker |
| Kết quả | Pass |

### Viewport đã kiểm tra

| Màn hình | Viewport | Kết quả |
|---|---:|---|
| Login | Desktop Chrome 1280 × 720 | Pass |
| Login | Mobile 390 × 844 | Pass |
| First login/email OTP | Mobile 390 × 844 | Pass; card cuộn nội bộ khi nội dung dài |
| Forgot password | Desktop 1440 × 900 | Pass |
| Dashboard có đăng nhập | Desktop 1440 × 900 | Pass |
| Dashboard có đăng nhập | Mobile 390 × 844 | Pass; bottom navigation hoạt động và trang không cuộn ngang |

### Kết quả kỹ thuật

| Kiểm tra | Kết quả |
|---|---|
| Build Blazor WASM | Pass, 0 warning/0 error |
| `docker compose config --quiet` | Pass |
| Build image `hau/frontend:local` | Pass |
| Container `hau_frontend` | `running` |
| `GET http://localhost:5227/` | HTTP 200 |
| `GET /css/app.css` | HTTP 200, có theme HAU glassmorphism |
| `GET /images/hau-logo.png` | HTTP 200, `image/png` |
| Logo trên sidebar/auth/loading/favicon | Pass |
| Ba cấp glassmorphism | Pass |
| Safari prefix `-webkit-backdrop-filter` | Có |
| Fallback không hỗ trợ blur | Có |
| `prefers-reduced-motion` | Có |
| Desktop/tablet/mobile responsive | Pass |

### Kết luận

- Giao diện mới không thay đổi API hoặc nghiệp vụ hiện có.
- Nội dung chính dùng nền kính sáng đủ độ tương phản; các hiệu ứng trang trí không nhận sự kiện chuột.
- Mobile khóa overflow ngang của trang và chỉ cho phép thanh điều hướng đáy tự cuộn khi số mục vượt chiều rộng.

## TC-FE-STYLE-021 — Font Inter, card một màu và icon đồng bộ

| Mục | Nội dung |
|---|---|
| Ngày chạy | 27/09/2026 |
| Phạm vi | Frontend Blazor WASM + Nginx Docker + Chromium |
| Mục tiêu | Xác nhận giao diện dùng Inter, card không dùng gradient và icon chức năng cùng màu xanh HAU |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Font `Inter` trên body, button, input, select, textarea | Pass |
| Stat card và card nội dung dùng nền kính một màu | Pass |
| Login card và modal không dùng gradient | Pass |
| Icon chức năng thống nhất `#0879BD` | Pass |
| Thanh nhấn stat card thống nhất `#0093DD` | Pass |
| Màu cảnh báo/lỗi/trạng thái nghiệp vụ vẫn được giữ | Pass |
| Build Frontend | Pass, 0 warning/0 error |
| Docker Compose config/build/up | Pass |

| Frontend | HTTP 200, container `running` |
| Playwright dashboard desktop 1440 × 900 | Pass |
| Playwright login mobile 390 × 844 | Pass |

## TC-FE-CSS-022 — CSS isolation riêng cho từng page

| Mục | Nội dung |
|---|---|
| Ngày chạy | 27/09/2026 |
| Phạm vi | Frontend Blazor WASM + Nginx Docker + Chromium |
| Mục tiêu | Xác nhận mỗi page có CSS riêng, scoped CSS được đóng gói và giao diện không bị lỗi sau refactor |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Số page Razor trong `Frontend/Pages` | 13 |
| Số page có file `.razor.css` cùng tên | 13/13, pass |
| Static inline style còn lại | 0 |
| Inline style động theo runtime | 3, đúng chủ đích |
| Build Frontend | Pass, 0 warning/0 error |
| `docker compose config --quiet` | Pass |
| Build/recreate image `frontend` | Pass |
| `GET http://localhost:5227/` | HTTP 200 |
| `GET /HauDocumentApp.styles.css` | HTTP 200 |
| Marker page trong CSS bundle Docker | 13/13 |
| Playwright Dashboard desktop 1440 × 900 | Pass |
| Playwright Admin Users desktop 1440 × 900 | Pass |
| Playwright Admin Departments desktop 1440 × 900 | Pass |
| Playwright Login mobile 390 × 844 | Pass |

### Kết luận

- CSS đặc thù của page được giới hạn bằng Blazor CSS isolation và không còn trộn trong markup.
- Style dùng chung vẫn nằm tại `wwwroot/css/app.css`, tránh sao chép giữa các page.
- Ba inline style còn lại đều nhận giá trị từ trạng thái runtime, không phải style tĩnh bị bỏ sót.

## TC-FE-DASHBOARD-023 — Dashboard theo mẫu HAU Docs

| Mục | Nội dung |
|---|---|
| Ngày chạy | 27/09/2026 |
| Phạm vi | Dashboard Blazor WASM + Nginx Docker + Chromium |
| Mục tiêu | Xác nhận Dashboard mới bám bố cục mẫu, hiển thị đủ dữ liệu và responsive |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Build Frontend | Pass, 0 warning/0 error |
| Docker Compose config/build/up | Pass |
| Frontend và `HauDocumentApp.styles.css` | HTTP 200 |
| Đăng nhập Admin và mở Dashboard | Pass |
| Tiêu đề và lời chào | Pass |
| Typography đề mục sau khi thu nhỏ | Pass |
| Thẻ thống kê desktop/tablet/mobile | `min-height: 100px`, `padding: 15px`, bo góc `10px`, pass |
| Sidebar mobile | Bo góc `10px`, pass |
| Màu sidebar/menu active/avatar do người dùng cấu hình | Giữ nguyên, pass |
| 4 thẻ thống kê Admin | Pass |
| Giá trị mặc định khi API trả `null` | Hiển thị `0`, pass |
| Khối `Thao tác nhanh` | Pass |
| Desktop 1440 × 900 | 4 cột, pass |
| Mobile 390 × 844 | 1 cột, bottom navigation hoạt động, pass |

### Kết luận

- Dashboard mới giữ nguyên phân quyền và các thao tác nghiệp vụ hiện có.
- Style chỉ nằm trong `Dashboard.razor.css`, không làm thay đổi bố cục của các page khác.

## TC-FE-ACCOUNT-RESPONSIVE-024 — Popup tài khoản và ba trạng thái chiều rộng

| Mục | Nội dung |
|---|---|
| Ngày chạy | 27/09/2026 |
| Phạm vi | Frontend Blazor WASM + IdentityService + Nginx Docker + Chromium |
| Mục tiêu | Xác nhận không còn sidebar compact, popup tài khoản hoạt động và màn hình quá hẹp bị khóa |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Build Frontend | Pass, 0 warning/0 error |
| Docker Compose config/build/up | Pass |
| Desktop 1440 × 900 | Sidebar đầy đủ, pass |
| Popup user card | Có `Chỉnh sửa thông tin` và `Đăng xuất`, pass |
| `GET /api/users/me` từ modal | Tải đúng hồ sơ Admin, pass |
| Lưu lại hồ sơ không đổi qua `PUT /api/users/{id}` | Pass |
| Đăng xuất từ popup | Về `/login`, pass |
| Viewport 900 × 800 | Bottom navigation, không có sidebar compact, pass |
| Viewport 340 × 700 | Ứng dụng bị khóa, hiện cảnh báo tối thiểu 450px, pass |
| Màu sidebar/menu/avatar do người dùng cấu hình | Giữ nguyên, pass |

### Kết luận

- Layout chỉ còn desktop, mobile và trạng thái không hỗ trợ khi quá hẹp.
- Popup tài khoản sử dụng API thật; người dùng không được sửa vai trò hoặc trạng thái tài khoản từ giao diện này.

## TC-FE-MOBILE-TASKBAR-025 — Nội dung cuộn phía sau taskbar mobile

| Mục | Nội dung |
|---|---|
| Ngày chạy | 27/09/2026 |
| Phạm vi | Frontend Blazor WASM + Nginx Docker + Chromium mobile |
| Mục tiêu | Nội dung được phép cuộn phía sau taskbar nhưng phần tử cuối không bị taskbar che |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Viewport | 390 × 844 |
| Chiều cao taskbar | 68px |
| Khoảng cách taskbar tới đáy | 10px |
| Padding đáy `.main-content` | 0px, pass |
| Padding đáy `.page-container` | 106px, pass |
| Vùng nội dung nằm phía sau taskbar | Chồng 78px, pass |
| Khoảng hở giữa nội dung cuối và taskbar sau khi cuộn hết | 37px, pass |
| Build Frontend | Pass, 0 warning/0 error |
| Docker Compose config/build/up | Pass |

### Kết luận

- Taskbar tiếp tục nổi ở vị trí người dùng đã cấu hình.
- Nội dung không bị cắt sớm trước taskbar và phần tử cuối vẫn truy cập được hoàn toàn.

## TC-FE-VIEWPORT-LIMIT-026 — Chiều rộng tối thiểu 450px

| Mục | Nội dung |
|---|---|
| Ngày chạy | 27/09/2026 |
| Phạm vi | Frontend Blazor WASM + Nginx Docker + Chromium |
| Mục tiêu | Xác nhận chính xác biên khóa ứng dụng mới tại 450px |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Viewport 449 × 800 | Ứng dụng bị khóa, pass |
| Nội dung cảnh báo tại 449px | Yêu cầu chiều rộng tối thiểu 450px, pass |
| Form đăng nhập tại 449px | Bị ẩn và không thể thao tác, pass |
| Viewport 450 × 800 | Ứng dụng hoạt động, pass |
| Đăng nhập tại 450px | Pass |
| Navigation tại 450px | Bottom navigation dạng mobile, pass |
| Build Frontend | Pass, 0 warning/0 error |
| Docker Compose config/build/up | Pass |

## TC-FE-SIGN-BACK-027 — Nút quay lại trang xem chữ ký

| Mục | Nội dung |
|---|---|
| Ngày chạy | 27/09/2026 |
| Phạm vi | Frontend Blazor WASM + Nginx Docker + Chromium |
| Mục tiêu | Xác nhận trang ký số luôn có lối quay về công văn tương ứng |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Chi tiết chữ ký, viewport 1440 × 900 | Nút `Quay lại` hiển thị, pass |
| Chi tiết chữ ký, viewport 450 × 844 | Nút `Quay lại` hiển thị toàn chiều rộng, pass |
| Đích từ `/signatures/{docId}` | `/documents/{docId}`, pass |
| Đích từ `/signatures` | `/documents`, pass |
| Build Frontend | Pass, 0 warning/0 error |
| Docker Compose config/build/up | Pass |
| Playwright Chromium | Pass 3/3 |

## TC-FE-BUTTON-COLOR-028 — Màu nền cố định cho nút primary

| Mục | Nội dung |
|---|---|
| Ngày chạy | 27/09/2026 |
| Phạm vi | Frontend Blazor WASM + Nginx Docker + Chromium |
| Mục tiêu | Xác nhận nút primary dùng nền `#304e8a` và không còn gradient |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| CSS `.btn-primary` trạng thái thường | `background-color: rgb(48, 78, 138)`, pass |
| CSS `.btn-primary` trạng thái hover | `background-color: rgb(48, 78, 138)`, pass |
| `background-image` ở trạng thái thường/hover | `none`, pass |
| Desktop 1440 × 900 | Pass |
| Mobile 450 × 844 | Pass |
| Build Frontend | Pass, 0 warning/0 error |
| Docker Compose config/build/up | Pass |
| Playwright Chromium | Pass 2/2 |

## TC-FE-CERT-USER-PICKER-029 — Tìm người dùng khi cấp chứng thư

| Mục | Nội dung |
|---|---|
| Ngày chạy | 27/09/2026 |
| Phạm vi | Frontend Blazor WASM + IdentityService + Nginx Docker + Chromium |
| Mục tiêu | Cấp chứng thư bằng cách tìm và chọn người dùng mà không cần biết GUID |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Tìm theo họ tên | Pass |
| Hai người dùng có cùng họ tên | Hiển thị đủ 2 mục riêng, pass |
| Thông tin phân biệt trong gợi ý | Username và email, pass |
| Chọn người dùng thứ hai trong danh sách trùng tên | Pass |
| GUID gửi tới API cấp chứng thư | Đúng GUID của mục thứ hai, pass |
| Tìm trực tiếp bằng GUID | Pass |
| Viewport mobile 450 × 844 | Danh sách nằm trong màn hình, pass |
| Build Frontend | Pass, 0 warning/0 error |
| Docker Compose config/build/up | Pass |
| Playwright Chromium | Pass 3/3 |

## TC-FE-CERT-TYPE-DROPDOWN-030 — Dropdown loại chứng thư tùy biến

| Mục | Nội dung |
|---|---|
| Ngày chạy | 27/09/2026 |
| Phạm vi | Frontend Blazor WASM + Nginx Docker + Chromium |
| Mục tiêu | Đồng bộ ô chọn loại chứng thư với giao diện gợi ý người dùng |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| `<select>` mặc định trong modal | Đã loại bỏ, pass |
| Giá trị mặc định | `Cá nhân (Personal)`, pass |
| Danh sách tùy biến | Hiển thị đủ 2 lựa chọn, pass |
| Chọn `Pháp nhân (Organization)` | Cập nhật đúng và đóng danh sách, pass |
| Nền và bo góc so với ô tìm người dùng | Đồng bộ, pass |
| Thuộc tính ARIA | Xuất đúng `true`/`false`, pass |
| Viewport mobile 450 × 844 | Danh sách nằm trong màn hình, pass |
| Build Frontend | Pass, 0 warning/0 error |
| Docker Compose config/build/up | Pass |
| Playwright Chromium | Pass 2/2 |

## TC-FE-CUSTOM-SELECT-031 — Chuẩn hóa các ô chọn frontend

| Mục | Nội dung |
|---|---|
| Ngày chạy | 28/09/2026 |
| Phạm vi | Frontend Blazor WASM + IdentityService + Nginx Docker + Chromium |
| Mục tiêu | Thay toàn bộ select native bằng dropdown đồng bộ với ô tìm người dùng |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Thẻ `<select>` còn lại trong page/shared | 0, pass |
| Dropdown vai trò | 6 lựa chọn, hiển thị đúng vai trò Admin, pass |
| Mapping response `roles[]` | Hiển thị `Quản trị viên`, pass |
| Dropdown phòng ban cha | Dữ liệu động hiển thị, pass |
| Dropdown loại văn bản | Dữ liệu động và binding giá trị, pass |
| Bộ lọc trạng thái công văn | Chọn `Nháp` và tải lại danh sách, pass |
| Bộ lọc loại công văn | Hiển thị danh sách tùy biến, pass |
| Nền và bo góc so với input | Đồng bộ, pass |
| Viewport mobile 450 × 844 | Dropdown nằm trong màn hình, pass |
| Build Frontend | Pass, 0 warning/0 error |
| Docker Compose config/build/up | Pass |
| Playwright Chromium | Pass 5/5 |

## TC-FE-CUSTOM-SELECT-032 — Dropdown dài không che thao tác modal

| Mục | Nội dung |
|---|---|
| Ngày chạy | 28/09/2026 |
| Phạm vi | Frontend Blazor WASM + Nginx Docker + Chromium |
| Mục tiêu | Xác nhận dropdown vai trò giữ đúng mẫu loại chứng thư và không che phần chân modal |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Hướng mở danh sách | Mở xuống giống dropdown loại chứng thư, pass |
| Chiều cao danh sách vai trò | Giới hạn 160px và cuộn bên trong, pass |
| Nút `Hủy` / `Lưu thay đổi` khi danh sách mở | Vẫn hiển thị, không bị che, pass |
| Chọn mục cuối `Ban Giám hiệu` | Cuộn, chọn đúng giá trị và tự đóng danh sách, pass |
| Mapping vai trò hiện tại từ `roles[]` | Hiển thị `Quản trị viên`, pass |
| Build Frontend | Pass, 0 warning/0 error |
| Docker Compose config/build/up | Pass |
| Playwright Chromium | Pass 1/1 |

## TC-FE-CUSTOM-SELECT-CACHE-033 — Tải đúng CSS dropdown sau khi triển khai

| Mục | Nội dung |
|---|---|
| Ngày chạy | 28/09/2026 |
| Phạm vi | Frontend Blazor WASM + Nginx Docker + Chromium |
| Mục tiêu | Không để trình duyệt ghép WASM mới với stylesheet CSS isolation cũ |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| URL `app.css` và `HauDocumentApp.styles.css` | Có phiên bản `v=20260928-2`, pass |
| Cache header của `index.html` | `no-cache, no-store, must-revalidate`, pass |
| Cache header của stylesheet | `no-cache, no-store, must-revalidate`, pass |
| Dropdown phòng ban cha | Danh sách nền trắng, vị trí absolute, item flex toàn chiều rộng, pass |
| Dropdown vai trò | CSS isolation được áp dụng, pass |
| Dropdown bộ lọc công văn | CSS isolation được áp dụng, pass |
| Build Frontend | Pass, 0 warning/0 error |
| Docker Compose config/build/up | Pass |
| Playwright Chromium | Pass 1/1 |

## TC-FE-ROLE-DROPDOWN-OVERLAY-034 — Dropdown vai trò vượt khung modal

| Mục | Nội dung |
|---|---|
| Ngày chạy | 28/09/2026 |
| Phạm vi | Frontend Blazor WASM + Nginx Docker + Chromium |
| Mục tiêu | Danh sách vai trò không bị cắt bởi đáy modal và vẫn cuộn được |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Danh sách vượt đường biên dưới modal | Hiển thị đầy đủ trên lớp nội dung, pass |
| Chiều cao tối đa | Không quá 160px, pass |
| Cuộn danh sách | `overflow-y: auto`, pass |
| Cụm nút thao tác bên phải | Không bị danh sách che, pass |
| Chọn mục cuối `Ban Giám hiệu` | Cuộn, chọn đúng và tự đóng danh sách, pass |
| Build Frontend | Pass, 0 warning/0 error |
| Docker Compose config/build/up | Pass |
| Playwright Chromium | Pass 1/1 |

## TC-FE-RESPONSIVE-MODAL-035 — Popup responsive và giới hạn viewport

| Mục | Nội dung |
|---|---|
| Ngày chạy | 28/09/2026 |
| Phạm vi | Frontend Blazor WASM + Nginx Docker + Chromium |
| Mục tiêu | Popup luôn trên navigation, vừa viewport và chỉ cho dùng từ kích thước 450 × 720 |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Viewport 449 × 720 | Ứng dụng bị khóa, pass |
| Viewport 450 × 719 | Ứng dụng bị khóa, pass |
| Viewport 450 × 720 | Ứng dụng hoạt động, pass |
| Modal người dùng tại 450 × 720 | Toàn màn hình, body cuộn, header/footer hiển thị, pass |
| Popup chỉnh sửa tài khoản tại 450 × 720 | Toàn màn hình và nằm trên taskbar, pass |
| Popup chứng thư dài tại 450 × 720 | Header/footer cố định, body cuộn, pass |
| Viewport 1000 × 744 | Modal toàn màn hình và nằm trên dashboard/sidebar, pass |
| Viewport 1366 × 768 | Modal toàn màn hình, pass |
| Viewport 1366 × 900 | Modal căn giữa, overlay vẫn nằm trên sidebar, pass |
| Build Frontend | Pass, 0 warning/0 error |
| Docker Compose config/build/up | Pass |
| Playwright Chromium | Pass 7/7 |

## TC-FE-VIEWPORT-HEIGHT-036 — Chiều cao viewport tối thiểu 500px

| Mục | Nội dung |
|---|---|
| Ngày chạy | 28/09/2026 |
| Phạm vi | Frontend Blazor WASM + Nginx Docker + Chromium |
| Mục tiêu | Cho phép sử dụng ứng dụng từ kích thước 450 × 500 và giữ popup sử dụng được trên màn hình thấp |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Viewport 450 × 499 | Ứng dụng bị khóa, pass |
| Viewport 450 × 500 | Ứng dụng hoạt động, pass |
| Viewport 449 × 600 | Ứng dụng bị khóa, pass |
| Viewport 450 × 600 | Ứng dụng hoạt động, pass |
| Modal người dùng tại 450 × 500 | Toàn màn hình, body cuộn, header/footer hiển thị, pass |
| Modal người dùng tại 1366 × 500 | Toàn màn hình, footer hiển thị, pass |
| Build Frontend | Pass, 0 warning/0 error |
| Docker Compose config/build/up | Pass |
| Playwright Chromium | Pass 4/4 |

## TC-FE-MODAL-BREAKPOINT-037 — Phân biệt popup desktop và toàn màn hình

| Mục | Nội dung |
|---|---|
| Ngày chạy | 28/09/2026 |
| Phạm vi | Frontend Blazor WASM + Nginx Docker + Chromium |
| Mục tiêu | Chỉ dùng modal toàn màn hình trên mobile/máy tính nhỏ, giữ popup căn giữa trên desktop thông thường |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Mobile 450 × 500 | Modal toàn màn hình, pass |
| Desktop nhỏ 1200 × 800 | Modal toàn màn hình, pass |
| Desktop rất thấp 1366 × 650 | Modal toàn màn hình, pass |
| Desktop mặc định 1366 × 768 | Popup căn giữa, không chiếm toàn màn hình, pass |
| Desktop lớn 1920 × 1080 | Popup căn giữa, không chiếm toàn màn hình, pass |
| Build Frontend | Pass, 0 warning/0 error |
| Docker Compose config/build/up | Pass |
| Playwright Chromium | Pass 5/5 |

## TC-SIGN-CERT-SELF-SERVICE-038 — Người ký tự tạo chứng thư số

| Mục | Nội dung |
|---|---|
| Ngày chạy | 28/09/2026 |
| Phạm vi | SignService + API Gateway + Frontend Blazor WASM + Nginx Docker + Chromium |
| Mục tiêu | Cho Manager/Ban Giám hiệu tự tạo chứng thư của chính mình mà không mở quyền cấp cho người khác |
| Kết quả | Pass |

### Kết quả API

| Kiểm tra | Kết quả |
|---|---|
| Manager tự cấp chứng thư | HTTP 200; `UserId`, username, họ tên lấy từ JWT; loại `Personal`, pass |
| Manager tự cấp lần hai khi chứng thư còn hạn | HTTP 409, pass |
| Manager gọi API Admin để cấp cho người khác | HTTP 403, pass |
| Clerk tự cấp chứng thư | HTTP 403, pass |
| Ban Giám hiệu tự cấp chứng thư | HTTP 200; loại `Organization`, pass |
| Admin cấp chứng thư bằng DTO đã đồng bộ | HTTP 200, pass |
| Admin lấy danh sách chứng thư thật | Có đủ chứng thư vừa cấp, pass |
| Thu hồi dữ liệu chứng thư test | Pass |

### Kết quả giao diện và đóng gói

| Kiểm tra | Kết quả |
|---|---|
| Trang `Chứng thư của tôi` của Manager | Hiển thị trạng thái chưa có chứng thư và nút tự tạo, pass |
| Xác nhận tự tạo trên desktop 1366 × 768 | Modal căn giữa, pass |
| Tạo và hiển thị chứng thư trên giao diện | Chủ thể, loại cá nhân, trạng thái hoạt động hiển thị đúng, pass |
| Modal Admin tìm/chọn người dùng | Hiển thị đúng người dùng và thời hạn mặc định 365 ngày, pass |
| Trường mật khẩu khóa không được xử lý | Đã loại bỏ khỏi form, pass |
| Build toàn solution | Pass, 0 warning/0 error |
| xUnit toàn solution | Pass 36/36 |
| Docker Compose config/build/up | Pass |
| Kiểm tra API qua Gateway | Pass 7/7 |
| Playwright Chromium | Pass 2/2 |

## TC-FE-CERT-DEPARTMENT-FILTER-039 — Lọc người dùng theo đơn vị khi cấp chứng thư

| Mục | Nội dung |
|---|---|
| Ngày chạy | 28/09/2026 |
| Phạm vi | IdentityService + API Gateway + Frontend Blazor WASM + PostgreSQL + Docker + Chromium |
| Mục tiêu | Thu hẹp danh sách tìm người dùng theo Trường/Ban/Khoa trước khi Admin cấp chứng thư |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Dropdown cơ cấu tổ chức | Hiển thị `Tất cả đơn vị`, Trường HAU và Phòng Tổng hợp theo đúng cấp cây, pass |
| Chọn đơn vị con rồi tìm `admin` | Không trả Admin thuộc đơn vị gốc, pass |
| Chuyển về `Tất cả đơn vị` rồi tìm `admin` | Trả đúng System Administrator, pass |
| Chọn đơn vị cha | Trả người dùng thuộc đơn vị con, pass |
| Kết hợp `departmentId` và từ khóa | Lọc tại API/PostgreSQL, pass |
| Thông tin gợi ý | Hiển thị họ tên, username, email và đơn vị, pass |
| Modal mobile 450 × 500 | Toàn màn hình, dropdown dùng được, footer không bị che, pass |
| User tạm dùng kiểm thử | Đã xóa, pass |
| Build toàn solution | Pass, 0 warning/0 error |
| xUnit toàn solution | Pass 37/37 |
| Docker Compose config/build/up | Pass |
| API Docker/Gateway | Pass 3/3 |
| Playwright Chromium | Pass 2/2 |

## TC-FE-CERT-ORG-CASCADE-040 — Tách bộ lọc cơ cấu tổ chức khi cấp chứng thư

| Mục | Nội dung |
|---|---|
| Ngày chạy | 28/09/2026 |
| Phạm vi | Frontend Blazor WASM + API Gateway + IdentityService + Docker + Chromium |
| Mục tiêu | Tách Trường, Ban/Khoa/Phòng và đơn vị cấp dưới thành các lựa chọn phụ thuộc để thu hẹp phạm vi tìm người dùng |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Trạng thái ban đầu | Ô Trường dùng được; hai cấp sau bị khóa, pass |
| Chọn Trường HAU | Ô Ban/Khoa/Phòng được mở và hiển thị `Phòng Tổng hợp (TH)`, pass |
| Chọn Phòng Tổng hợp | Ô đơn vị cấp dưới tiếp tục khóa vì dữ liệu hiện tại không có cấp con, pass |
| Tìm `admin` trong Phòng Tổng hợp | Không trả Admin thuộc Trường, pass |
| Chuyển về toàn bộ đơn vị trực thuộc của Trường | Tìm thấy `System Administrator`, pass |
| Đổi cấp cha | Xóa cấp con, người dùng đã chọn và kết quả tìm cũ, pass |
| Mobile 450 × 500 | Modal toàn màn hình; ba dropdown và footer đều truy cập được, pass |
| Build Frontend | Pass, 0 warning/0 error |
| xUnit toàn solution | Pass 37/37 |
| Docker Compose config/build/up | Pass |
| Playwright Chromium | Pass 2/2 |

## TC-DOC-041 — Kiểm tra bộ tài liệu theo thư mục

| Mục | Nội dung |
|---|---|
| Ngày chạy | 28/09/2026 |
| Phạm vi | Tài liệu Markdown + Docker Compose |
| Mục tiêu | Xác nhận bộ tài liệu có đủ ba nhóm, liên kết dùng được và hướng dẫn gọi đúng service thực tế |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Thư mục test case | Có README, danh mục và mẫu test case, pass |
| Thư mục triển khai | Có hướng dẫn hạ tầng và 6 thành phần ứng dụng, pass |
| Thư mục nhật ký | Có README, nhật ký tóm tắt và mẫu ghi, pass |
| Liên kết tương đối trong `tai_lieu` | 15 file Markdown, 0 liên kết hỏng, pass |
| `docker compose config --quiet` | Pass |
| Tên service trong tài liệu | Đối chiếu đủ 11 service/hạ tầng từ Compose, pass |
| Build ứng dụng | Không áp dụng vì không thay code hoặc cấu hình runtime |

## TC-AUTH-DEFAULT-042 — Xác nhận tài khoản mặc định để kiểm thử

| Mục | Nội dung |
|---|---|
| Ngày chạy | 28/09/2026 |
| Phạm vi | IdentityService + ApiGateway + PostgreSQL + tài liệu |
| Mục tiêu | Xác nhận tài khoản ứng dụng seed và thông tin hạ tầng local trong tài liệu đúng với hệ thống đang chạy |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Seed trong `AppDbContext` | Chỉ có user `admin`, role `Admin`, pass |
| Login `admin` qua Gateway | Pass |
| Role trả về | `Admin`, pass |
| Logout và thu hồi phiên kiểm tra | Pass |
| Tài khoản PostgreSQL/MinIO/Mailpit | Khớp default trong Docker Compose, pass |
| Phân biệt user test timestamp | Đã ghi rõ không phải tài khoản mặc định, pass |
| Build ứng dụng | Không áp dụng vì không thay code hoặc cấu hình runtime |

## TC-FE-DASHBOARD-QUICK-ACTIONS-043 — Theo dõi hệ thống và thông báo từ Dashboard

| Mục | Nội dung |
|---|---|
| Ngày chạy | 28/09/2026 |
| Phạm vi | Frontend Blazor WASM + Nginx Docker + Chromium |
| Mục tiêu | Bổ sung lối vào log, hoạt động ứng dụng và thông báo mà không tạo route chết hoặc hiển thị dữ liệu giả |
| Kết quả | Pass |

### Kết quả

| Kiểm tra | Kết quả |
|---|---|
| Quick action `Nhật ký hệ thống` | Hiển thị cho Admin, mở `/admin/system-logs`, pass |
| Quick action `Hoạt động ứng dụng` | Hiển thị cho Admin, mở `/admin/activity`, pass |
| Quick action `Trung tâm thông báo` | Mở `/notifications`, pass |
| Phân quyền trang log/hoạt động | Có `[Authorize(Roles = "Admin")]`, pass |
| Trạng thái dữ liệu | Ghi rõ là giao diện nền chờ API, không hiển thị log/audit giả, pass |
| Desktop 1366 × 768 | Ba route và điều hướng hoạt động, pass |
| Mobile 450 × 500 | Nút truy cập được, trang không tràn ngang, pass |
| Build Frontend | Pass, 0 warning/0 error |
| xUnit toàn solution | Pass 37/37 |
| Docker Compose config/build/up | Pass |
| Playwright Chromium | Pass 2/2 |

## TC-DOC-SYNC-044 — Đồng bộ tài liệu với source và nhật ký

| Mục | Nội dung |
|---|---|
| Ngày chạy | 05/10/2026 |
| Phạm vi | `tai_lieu`, `tiendo`, source frontend/backend và Docker Compose |
| Mục tiêu | Loại bỏ các tổng kết cũ, ghi đúng phần đã có/chưa hoàn tất và bảo toàn kết quả lịch sử |
| Dữ liệu test | Không tạo user, document, email hoặc token |
| Kết quả | Pass — kiểm tra tài liệu/source, không chạy lại runtime |

### Các bước

1. Đối chiếu trang frontend, CSS, các service frontend và controller/backend liên quan.
2. Đối chiếu `DocumentAction`, `AuthStoreInitializer`, cấu hình Compose và nhật ký Gmail.
3. Cập nhật tổng kết, việc còn lại, danh mục test và mốc đồng bộ.
4. Dùng PowerShell đọc toàn bộ Markdown trong hai thư mục, bỏ code fence và kiểm tra đích liên kết tương đối bằng `Test-Path`.
5. Đếm page `.razor` trong `Frontend/Pages` và kiểm tra file `.razor.css` cùng tên.
6. Kiểm tra Compose, tính nhất quán mã test/danh mục và diff whitespace.

### Kết quả

| Kiểm tra | Mong đợi | Thực tế | Trạng thái |
|---|---|---|---|
| Markdown được kiểm tra | Toàn bộ hai thư mục | 24 file | Pass |
| Liên kết tương đối | Đích tồn tại | 26 liên kết, 0 lỗi | Pass |
| Page và CSS isolation | Mỗi page có CSS cùng tên | 17/17 | Pass |
| Cấu hình Compose | Cú pháp hợp lệ, đúng tên thành phần | 11 service/hạ tầng | Pass |
| Danh mục test | Mã khớp file chi tiết | 44 mã, `001`–`044` | Pass |
| Gmail | Khớp nhật ký Công việc số 17 | Gửi/nhận thành công; reset OTP thật chưa chạy | Pass |
| Dashboard, log/audit/thông báo | Không coi dữ liệu mặc định/trang nền là tính năng backend hoàn tất | Ghi rõ API chưa có/chưa kết nối | Pass |
| Workflow/DB/CSS | Khớp source hiện tại | Có `SubmitDirector`, initializer email/token và lớp modal/toast mới | Pass |
| Lịch sử kiểm thử | Giữ kết quả tại thời điểm chạy | Không đổi viewport hoặc số test cũ thành kết quả mới | Pass |
| Rà soát phần đã làm | Không đưa tính năng đã Pass trở lại danh sách chưa triển khai | Mục 7 ghi riêng phần đã làm, triển khai thiếu, kiểm thử bổ sung và đề xuất; ký/workflow và OCR đã hoàn thành theo luồng kiểm thử | Pass |
| Kiểm tra lại sau rà soát mục 7 | Liên kết và mã test dẫn chứng tồn tại | 24 file, 28 liên kết hợp lệ; 13 mã test dẫn chứng có mục chi tiết; danh mục vẫn đủ 44 test case | Pass |
| `git diff --check` | Không lỗi whitespace | Không lỗi | Pass |

### Lệnh đã chạy

```powershell
Get-ChildItem -LiteralPath tai_lieu,tiendo -Recurse -File -Filter *.md
Get-ChildItem -LiteralPath Frontend/Pages -Recurse -File -Filter *.razor
docker compose config --quiet
docker compose config --services
git -c safe.directory=E:/DigitalSign_OCRProject diff --check
```

Kiểm tra liên kết và đối chiếu mã test chạy bằng PowerShell với regex, `Get-Content` và `Test-Path`. Không build ứng dụng, recreate container, gửi email hoặc chạy lại API/UI; thay đổi chỉ gồm tài liệu. `tai_lieu/` bị `.gitignore` bỏ qua nên kiểm tra trực tiếp trên filesystem; `git diff --check` chỉ bao phủ file Git đang theo dõi.

## TC-DOC-EDIT-045 — Sửa metadata công văn và đồng bộ DTO frontend

| Mục | Nội dung |
|---|---|
| Ngày chạy | 05/10/2026 |
| Phạm vi | DocumentService, Frontend, IdentityService và Gateway trên Docker local |
| Mục tiêu | Tạo/sửa gửi đúng metadata, sửa đúng trạng thái và giữ file/OCR/workflow |
| Dữ liệu | 2 document/PDF tạm; Admin và user test Manager/Specialist |
| Kết quả | Pass |

### Các bước và kết quả

1. Login Admin, lấy loại văn bản, tạo document Draft và upload PDF trắng hợp lệ; đợi callback OCR trước khi sửa metadata.
2. PUT sửa tiêu đề/số hiệu/loại/ngày: HTTP 200, trim chuỗi, `MinioPath`/`OcrDataRaw`/`Status` không đổi; lịch sử `Update` có actor đúng JWT.
3. Xóa số hiệu/ngày bằng null: HTTP 200 và dữ liệu null; title trống hoặc loại không tồn tại: HTTP 400.
4. Manager sửa: HTTP 403. Unit test chặn đủ 5 trạng thái không phải Draft/Rejected; API PendingDeptReview trả 422, UI ẩn nút sửa. Sau reject, API cho sửa metadata và vẫn giữ Rejected.
5. Đăng nhập Admin trên trình duyệt; desktop sửa qua modal, request gửi đúng `DocNumber` và ngày DateOnly, heading cập nhật. Mobile 450×500 mở/chọn loại/hủy modal, chân modal thao tác được.
6. Specialist tạo công văn trên UI kèm PDF: POST 201, số hiệu/loại/ngày đúng; upload 200 unwrap response đúng; điều hướng đến chi tiết và có iframe PDF.

### Build và cách chạy lại

- Build solution: pass, 0 warning/0 error; xUnit 61/61 (Document 25, Identity 35, Sign 1 test rỗng).
- Docker image Identity/Document/Frontend build và recreate thành công; không đổi schema DB.
- Script chung cho `045`–`047`: `tests/document-edit-file-assignment.spec.cjs`, kết quả 13 nhóm smoke pass, gồm API và Playwright Chromium.
- Máy host dùng SDK 10 nhưng thiếu runtime 9: cài ASP.NET Core/runtime 9.0.20 vào `%TEMP%/hau-dotnet9`, không đổi hệ thống. Nếu host đã có runtime .NET 9 thì không cần các biến `DOTNET_*` dưới đây.

```powershell
dotnet build HAU_DigitalSign_OCR.slnx --no-restore --verbosity quiet
$env:DOTNET_ROOT = Join-Path $env:TEMP 'hau-dotnet9'
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:VSTEST_DOTNET_PATH = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
dotnet test HAU_DigitalSign_OCR.slnx --no-build --verbosity quiet
docker compose build identity-service document-service frontend
docker compose up -d --no-deps identity-service document-service frontend
npm.cmd install --prefix (Join-Path $env:TEMP 'hau-document-tools') playwright --no-audit --no-fund
$env:HAU_PLAYWRIGHT_MODULE = Join-Path $env:TEMP 'hau-document-tools/node_modules/playwright'
node tests/document-edit-file-assignment.spec.cjs
```

Script yêu cầu Node 20+, Chromium của Playwright và Docker CLI; nếu chưa có Chromium, dùng CLI Playwright `install chromium`. Tài khoản test có thể cấu hình qua `HAU_TEST_USERNAME`/`HAU_TEST_PASSWORD`, phải là Admin local đã hoàn tất first login. Không in token trong output.

### Dọn dẹp

Lần chạy hoàn tất xóa 2 document, 2 user và 2 object PDF tạo bởi script; logout các phiên test. Ảnh modal tại `tests/artifacts` (Git bỏ qua ảnh). Một lần chạy trước chạm rate limit Gateway 429: đã dọn dữ liệu theo GUID document chính xác bằng `HAU_CLEANUP_DOCUMENT`; script hiện xử lý Retry-After cho API và có chờ DocumentService sẵn sàng khi vừa recreate.

## TC-DOC-FILE-046 — Xem/tải PDF đúng object MinIO bằng JWT

| Mục | Nội dung |
|---|---|
| Ngày chạy | 05/10/2026 |
| Phạm vi | API file DocumentService và iframe/download Blazor trên Docker/Gateway |
| Mục tiêu | Đọc đúng PDF đã upload, xác thực và hỗ trợ xem/tải trong trình duyệt |
| Dữ liệu | PDF trắng hợp lệ do script tạo; object UUID khác document ID |
| Kết quả | Pass |

### Các bước và kết quả

1. GET file không JWT: 401. Document không có PDF hoặc document không tồn tại: 404.
2. Upload PDF, GET `/api/documents/{id}/file`: 200, `Content-Type: application/pdf`, `Cache-Control: no-store`; so byte-for-byte với file upload, hoàn toàn khớp.
3. GET `?download=true`: attachment với tên `.pdf`. Request `Range: bytes=0-9`: 206, dữ liệu đúng 10 byte đầu.
4. Unit test resolve path `documents/{object}`, object thô và path có slash; dùng tên object đã lưu, không tự tìm `{documentId}.pdf`.
5. Chi tiết UI hiển thị iframe `blob:`. Nút `Tải PDF` tạo download Chromium, tên `.pdf` và byte-for-byte khớp PDF gốc.

Chạy cùng script và build của `TC-DOC-EDIT-045`. File/blob trên UI không chứa JWT trong URL hoặc hostname MinIO nội bộ. Test xác nhận vận chuyển file và hiển thị iframe; không đánh giá chất lượng OCR của PDF scan thật. Dữ liệu PDF tạm đã xóa khỏi MinIO.

## TC-DOC-ASSIGN-047 — Phân công đúng người nhận và xử lý lỗi

| Mục | Nội dung |
|---|---|
| Ngày chạy | 05/10/2026 |
| Phạm vi | Identity danh bạ, Document assign, Frontend modal desktop/mobile |
| Mục tiêu | Payload `ToUserId` đúng, người nhận hoạt động, lịch sử và UI phản ánh kết quả thật |
| Dữ liệu | Admin và 2 user test Manager/Specialist; 1 document có PDF |
| Kết quả | Pass |

### Các bước và kết quả

1. GET `/api/users/assignees?search=admin`: tìm đúng tài khoản; DTO không chứa email/roles.
2. Payload cũ `assignedToId`, GUID rỗng hoặc người nhận không tồn tại: 400; tài khoản bị khóa: 400.
3. Assign đúng `toUserId`: 200, lịch sử `Assign` đúng actor, người nhận và comment; trạng thái document giữ nguyên.
4. Manager có quyền đọc danh bạ/phân công; Specialist phân công bị 403.
5. Unit test xác nhận không ghi phân công khi danh bạ 404, inactive, 500, 401, JSON lỗi, thiếu data, data null hoặc root array; lỗi dữ liệu/kết nối trả 503 theo controller. Unit test active recipient kiểm tra header Bearer và URL gọi Identity.
6. UI desktop 1366×768 và mobile 450×500 tìm Admin, chọn dropdown, nhập ghi chú, xác nhận; POST gửi đúng `toUserId`, modal đóng khi thành công. Kiểm tra ảnh cho thấy modal/nút thao tác nằm trong viewport.
7. Giả lập HTTP 503 trên request phân công bằng Playwright: UI giữ modal và hiện `Phân công thất bại`; bỏ giả lập rồi gửi lại thành công.
8. Trang chi tiết không có lỗi JavaScript trong lần kiểm tra. Ảnh `document-assignment-1366.png`, `document-assignment-450.png`, `document-edit-450.png` đã được xem lại.

Chạy cùng script/build/dọn dẹp của `TC-DOC-EDIT-045`. Named client `identity-directory` dùng `IdentityService:BaseUrl`, timeout 5 giây. Không chạy phép thử bằng cách tắt IdentityService thật; lỗi danh bạ được kiểm tra ở unit test, lỗi frontend được giả lập trên request test riêng.

## TC-DASHBOARD-STATS-048 — API thống kê toàn dữ liệu và phân quyền

| Mục | Nội dung |
|---|---|
| Ngày chạy | 05/10/2026 |
| Phạm vi | DocumentService, IdentityService, SignService, PostgreSQL và Gateway Docker local |
| Mục tiêu | Thống kê từ nguồn thật, đúng trạng thái/actor/ngày và không bị giới hạn phân trang |
| Dữ liệu | Admin seed, 4 user theo role, 22 document fixture và 1 chứng thư test |
| Kết quả | Pass |

### Các bước và kết quả

1. Login Admin, lấy baseline `GET /api/documents/stats`; so toàn bộ 11 trường với SQL PostgreSQL độc lập, khớp hoàn toàn. Ba API stats không JWT trả 401; thành công có `Cache-Control: no-store`.
2. Tạo Clerk/Specialist/Manager/BoardOfDirectors: mỗi role đọc stats công văn được, stats user/certificate trả 403. Tổng người dùng Admin khớp `SELECT count(*) FROM AppUsers`, không dùng số phần tử trang đầu.
3. Tạo 22 document của Specialist bằng API; chỉnh metadata/history trên đúng GUID fixture để có 7 trạng thái workflow, PDF chờ OCR, một document tạo từ hôm trước và 2 lần Assign cùng document bởi Manager. Không tạo object MinIO và không thực hiện ký PDF trong test thống kê này.
4. Tổng document tăng 22 (33 khi chạy, vượt page size 20), hôm nay tăng 21. Mỗi role so toàn bộ response với SQL độc lập, khớp; Specialist có 16 draft của tôi; Manager có 1 document đã phân công dù có 2 event Assign.
5. Admin stats chứng thư tăng 1 sau cấp Personal cho Manager test; so với danh sách cert còn hiệu lực theo NotBefore/NotAfter. Thu hồi: số giảm về baseline (4 khi chạy); Root CA không tính.
6. Thêm 4 test xUnit repository: database rỗng trả 0; đếm các status/OCR độc lập; ranh giới ngày Việt Nam đầu bao gồm/cuối loại trừ, không đếm IssuedDate hoặc resubmit mới thành ngày tạo; đúng creator và distinct assignment. Document không có Submit vẫn nằm trong tổng, không tính ngày tạo/của tôi.

### Build và lệnh chạy

- `dotnet build HAU_DigitalSign_OCR.slnx --no-restore --verbosity quiet`: 0 warning/0 error.
- xUnit toàn solution: 65/65 (Document 29, Identity 35, Sign 1 test rỗng); host dùng runtime 9.0.20 trong `%TEMP%/hau-dotnet9` như `TC-DOC-EDIT-045`.
- Build/recreate Docker `identity-service document-service sign-service frontend`: pass. Sau sửa phát hiện response thiếu trường, build/recreate riêng Frontend và chạy lại smoke trên image cuối.
- Không thêm schema/migration hay route Gateway: các route stats nằm dưới catch-all sẵn có.

```powershell
docker compose build identity-service document-service sign-service frontend
docker compose up -d --no-deps identity-service document-service sign-service frontend
$env:HAU_PLAYWRIGHT_MODULE = Join-Path $env:TEMP 'hau-document-tools/node_modules/playwright'
node tests/dashboard-statistics.spec.cjs
```

Script yêu cầu Node 20+, Playwright/Chromium đã cài, Docker CLI và stack local; kết nối PostgreSQL bằng `docker exec psql`, lấy tên database/user từ cấu hình container trong bộ nhớ, không in secret. Có thể cấu hình Admin qua `HAU_TEST_USERNAME`/`HAU_TEST_PASSWORD`.

### Dọn dẹp

Xóa đúng 22 GUID document/process fixture trong transaction PostgreSQL vì các fixture Published không thể xóa qua workflow API; không đụng document có sẵn. Xóa 4 user test, thu hồi chứng thư mới và logout các phiên test. Không tạo/xóa file MinIO, không gửi email. Kết quả script hoàn tất 11 nhóm smoke cho `048`–`049` và xác nhận cleanup.

## TC-FE-DASHBOARD-STATS-049 — Dashboard theo role, lỗi nguồn và làm mới

| Mục | Nội dung |
|---|---|
| Ngày chạy | 05/10/2026 |
| Phạm vi | Blazor Frontend, 3 API thống kê qua Gateway, Playwright Chromium |
| Mục tiêu | Hiển thị đúng chỉ số nghiệp vụ, không tự điền 0 khi API lỗi và phục hồi bằng làm mới |
| Viewport | Desktop 1366×768, mobile 450×500 |
| Kết quả | Pass |

### Các bước và kết quả

1. Đăng nhập Admin trên UI, đối chiếu từng card với response thật: tổng người dùng, tổng văn bản, chờ xử lý và chứng thư hoạt động đều khớp. Ảnh desktop/mobile đã được xem lại; mobile không tràn ngang.
2. Clerk: hôm nay UTC+7, đã upload/chưa OCR, Published. Specialist: Draft và đang chờ phê duyệt do chính mình tạo. Manager: PendingDeptReview toàn hệ thống và distinct văn bản do mình phân công. BoardOfDirectors: PendingDirectorSign và DirectorSigned/Published toàn hệ thống. Tất cả card khớp SQL theo actor JWT.
3. Các role ngoài Admin không phát request stats quản trị user/certificate. Cả 5 role không có lỗi JavaScript trong lần test.
4. Playwright giả lập certificate stats HTTP 503: card chứng thư hiện `—`, cảnh báo nguồn chưa tải được, số document vẫn giữ đúng.
5. Giả lập certificate stats HTTP 200 nhưng data thiếu trường: vẫn `—` và cảnh báo; không coi object rỗng là số 0 hợp lệ.
6. Giả lập thêm Document stats 503: các card công văn hiện `—`, tổng user vẫn giữ dữ liệu nguồn thành công.
7. Bỏ giả lập, bấm `Làm mới số liệu`: số thật trở lại, cảnh báo biến mất. Zero hợp lệ được format thành 0; null là `—`.

Chạy cùng script, build và cleanup của `TC-DASHBOARD-STATS-048`. Ảnh `tests/artifacts/dashboard-stats-1366.png`, `dashboard-stats-450.png` được Git bỏ qua. Các phép thử lỗi chỉ chặn response trong context Playwright của test; không dừng backend thật.

## TC-GW-LOGS-050 — Nhật ký HTTP thật, bộ lọc, quyền Admin và che dữ liệu

| Mục | Nội dung |
|---|---|
| Ngày chạy | 05/10/2026 |
| Phạm vi | ApiGateway, PostgreSQL, bốn downstream service, Blazor/Playwright |
| Mục tiêu | Log truy cập HTTP có nguồn thật, lọc/phân trang đúng và không lưu secret trong bảng nhật ký |
| Dữ liệu | Admin seed; 5 user theo role; fixture dùng chung với 051–052 |
| Mong đợi | Chỉ Admin đọc log/audit, không JWT 401; bộ lọc hợp lệ đúng dữ liệu, sai tham số 400; dữ liệu nhạy cảm không được lưu |
| Kết quả thực tế | Pass; 14 nhóm smoke dùng chung cả ba test case, 21 test Gateway |

### Các bước và kết quả

1. Không JWT: log/audit/notifications trả 401. Clerk/Specialist/Manager/BoardOfDirectors đọc hai API Admin trả 403. Response thành công có `Cache-Control: no-store`.
2. Gọi Identity `/users/me`, Document tạo/đọc, Sign certificate và OCR `/health`; mỗi bộ lọc service trả đúng nguồn; startup Gateway tạo nguồn thứ năm. POST `/api/ocr/process-upload` thiếu file trả 422, audit đúng ProcessOCR/Warning và route không bị che nhầm. Đây là HTTP qua Gateway và startup, không kiểm thử bộ thu gom stdout nội bộ.
3. Lọc service/level/actor/trace/from/to; phân trang audit page size 5, trang 2 đủ 5 với tổng >20. Trace tạo document nằm trong khoảng UTC và chỉ có một audit. Service/level/actor sai, from>to, loại thông báo sai trả 400.
4. Gửi sentinel riêng qua body comment/reason, URL segment/query; segment ngoài allowlist thành `/api/[unmapped]`. SQL kiểm tra event của đúng actor/document fixture không chứa sentinel, password test hoặc JWT test. Không lưu request body/query/header; full name actor vẫn được hiển thị theo danh bạ.
5. xUnit Gateway gồm 4 trường hợp che segment nhạy cảm, 2 route không log vòng lặp, giữ resource GUID/service và 14 trường hợp nhận diện action. Tổng 21 test Gateway Pass.
6. Playwright đăng nhập Admin, mở hai trang thật, lọc DocumentService/trace, còn đúng một card; audit lọc actor và sang trang 2. Giả lập riêng response 503: UI có cảnh báo. Desktop 1366×768, mobile 450×500 không tràn ngang; ảnh được xem lại, không lỗi JavaScript.

### Lệnh chạy chung cho 050–052

```powershell
dotnet build HAU_DigitalSign_OCR.slnx --no-restore --verbosity quiet
$env:DOTNET_ROOT = Join-Path $env:TEMP 'hau-dotnet9'
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:VSTEST_DOTNET_PATH = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
dotnet test HAU_DigitalSign_OCR.slnx --no-build --verbosity quiet
docker compose build api-gateway document-service frontend
docker compose up -d --no-deps api-gateway document-service frontend
$env:HAU_PLAYWRIGHT_MODULE = Join-Path $env:TEMP 'hau-document-tools/node_modules/playwright'
node tests/monitoring-notifications.spec.cjs
```

Build 0 warning/0 error; xUnit 86/86 (Gateway 21, Identity 35, Document 29, Sign 1 test rỗng). Host runtime 9.0.20 đã cài trong TEMP theo `TC-DOC-EDIT-045`; máy có runtime .NET 9 sẵn không cần ba biến runtime. Playwright path trỏ bản cài ngoài repo; nếu cài thông thường có thể bỏ biến module. Node 20+, Chromium, Docker CLI và stack localhost bắt buộc. Có thể override Admin qua `HAU_TEST_USERNAME`/`HAU_TEST_PASSWORD`; không in secret.

### Dọn dẹp chung

Script tạo 3 document, 5 user, 2 chứng thư Manager/Admin và 1 object PDF trắng. Xóa đúng document/process ID fixture trong PostgreSQL (Published không xóa qua workflow API), notices cascade; thu hồi chứng thư, logout các phiên, xóa user và đúng object GUID PDF trong MinIO. Xóa event chỉ theo actor/document fixture và trace hex đã quan sát; không xóa log khác. Cleanup đã hoàn tất. Không gửi email. SQL/MinIO lấy credential container trong bộ nhớ, không ghi credential vào output/tài liệu. Script xử lý HTTP 429 theo Retry-After và chờ health sau recreate Gateway.

## TC-AUDIT-051 — Audit công văn, callback OCR, upload và trace/persistence

| Mục | Nội dung |
|---|---|
| Ngày chạy | 05/10/2026 |
| Phạm vi | Gateway, DocumentService, Identity/Sign, PostgreSQL trigger |
| Mục tiêu | Audit đúng action/actor/resource, không lặp và giữ sau recreate Gateway |
| Mong đợi | HTTP và process cùng trace; callback trực tiếp có audit; recreate không mất/nhân bản lịch sử |
| Kết quả thực tế | Pass |

1. Tạo Draft bởi Specialist: header `X-Trace-Id` khớp audit trigger duy nhất; action `Create`, actor Specialist, resource document ID. Audit không bị ghi hai lần bởi Gateway và trigger.
2. PATCH OCR thẳng port 5049 bằng internal service token đọc từ container trong bộ nhớ: có audit `UpdateOCR` và thông báo người tạo. Đây là callback mô phỏng, không xác nhận chất lượng PaddleOCR trên scan thật.
3. Assign, Submit, DeptSign, SubmitDirector, DirectorSign, Publish và Reject bằng tài khoản theo vai trò tạo audit process thật. Đây là chuyển metadata workflow, không thực hiện ký PDF trong test này. Certificate issue do Admin: audit `IssueCertificate` có actor Admin, không nhầm người được cấp là actor.
4. Submit lại document Published: 422; audit `Submit`/Warning/HTTP 422 theo đúng trace, không gán tên tạo document chung.
5. Upload PDF trắng ở document thứ ba: có đúng một `UploadDocument` audit theo trace/resource dù upload không tạo process. Chờ callback OCR của PDF trước cleanup; không đánh giá chất lượng OCR bằng PDF trắng.
6. `docker compose up -d --no-deps --force-recreate api-gateway`, chờ `/health`: audit ID theo trace giữ nguyên, notifications ID/count/read state giữ nguyên, alert chứng thư không bị nhân bản. Initializer không backfill process cũ.

Schema mới được Gateway quản lý bằng `Monitoring/schema.sql` embedded resource, transaction DDL; không EF migration mới. Trigger process/audit/notification cùng transaction; metadata Document vẫn lưu riêng. Chạy/build/cleanup cùng `TC-GW-LOGS-050`.

## TC-NOTIFICATIONS-052 — Thông báo riêng, workflow/chứng thư, đọc/chưa đọc và polling

| Mục | Nội dung |
|---|---|
| Ngày chạy | 05/10/2026 |
| Phạm vi | API Gateway, PostgreSQL trigger, SignService, Blazor/Playwright |
| Mục tiêu | Đúng người nhận, chống sửa dữ liệu người khác, read state bền vững và tự cập nhật UI |
| Mong đợi | Thông báo riêng theo JWT, idempotent read; polling hiển thị mới; lỗi không giả thành thành công |
| Kết quả thực tế | Pass, desktop 1366×768/mobile 450×500 |

1. Assign cho Clerk: chỉ tài khoản đó có Assignment; callback OCR cho Specialist: OcrCompleted. Submit gửi ReviewRequested tới Manager hoạt động, SubmitDirector tới Ban Giám hiệu hoạt động. Signed hai bước tới người tạo; Reject tới người tạo; Publish tới người tạo và người đã được giao.
2. Lặp Assign 21 lần để vượt page size: Clerk có 23 thông báo, trang 1 đủ 20, trang 2 đủ 3, không trùng ID. Specialist thêm `userId` query của Clerk vẫn chỉ đọc dữ liệu của Specialist.
3. Specialist PATCH read notification của Clerk trả 404. Clerk đọc một thông báo hai lần: ReadAt không đổi. Lọc unread giảm đúng một; read-all chỉ giảm unread Clerk về 0, Specialist giữ nguyên.
4. Cấp chứng thư Manager hiệu lực 1 ngày: GET notifications tạo một CertificateExpiring, link `/certificates/me`. Đọc lại/poll không lặp và giữ ReadAt đã đọc. Tạo Admin fixture riêng, cấp chứng thư 1 ngày: alert riêng dẫn tới `/admin/certificates` vì Admin không có quyền trang chứng thư cá nhân. Không thay chứng thư của Admin seed. Kiểm tra này chạy khi đọc API, không xác nhận có worker cảnh báo nền.
5. Recreate Gateway giữ nguyên số bản ghi/ID/read state. Chứng thư còn hiệu lực ngoài cửa sổ 30 ngày không thuộc rule cảnh báo; expiry không phải trigger workflow.
6. UI Clerk có loại thông báo, checkbox chỉ chưa đọc, link đúng `/documents/{fixtureId}`. Giả lập PATCH read 503: card vẫn chưa đọc và hiện lỗi; bỏ giả lập, đọc thành công thì card biến mất khỏi bộ lọc unread. GET 503 có cảnh báo, làm mới phục hồi.
7. Tạo Assign mới khi trang đang mở: card mới tự xuất hiện trong chu kỳ polling 30 giây, timeout kiểm thử 40 giây; không bấm refresh.
8. Trì hoãn một request refresh 800ms và đổi loại sang Published trong khi đang tải: kết quả cuối đúng bộ lọc mới (0 unread Published), không hiển thị response cũ. Đổi lại Assignment thấy thông báo mới; read-all cập nhật UI, trở Dashboard và đóng context không lỗi JavaScript.
9. Ảnh desktop/mobile và card sau cuộn đã xem lại; không tràn ngang. Lỗi được giả lập trên context Playwright riêng, không dừng service thật. Timer/request bị hủy khi rời trang; chưa dùng SignalR/push.

Script/build/cleanup dùng chung `TC-GW-LOGS-050`. Ảnh trong `tests/artifacts` được Git bỏ qua; credentials và payload secret không xuất ra báo cáo.
