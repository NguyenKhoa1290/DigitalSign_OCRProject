# Sign Service

SignService cấp chứng thư số nội bộ, ký PDF và xác minh chữ ký. Service dùng iText7 + BouncyCastle, PostgreSQL và MinIO.

## Chạy local

Khởi động hạ tầng trước:

```bash
docker compose up -d postgres minio minio-init
```

Đảm bảo `JwtSettings` của SignService khớp với IdentityService/Gateway nếu chạy local bằng `dotnet run`.

```bash
dotnet run --project SignService/src/SignService.API/SignService.API.csproj
```

Port mặc định:

```text
http://localhost:5050
```

## Chạy bằng Docker

Compose ở root đã override JWT/MinIO/DB đúng cho môi trường Docker:

```bash
docker compose up -d sign-service
```

Hoặc chạy toàn bộ hệ thống:

```bash
docker compose up -d
```

## API chính

```text
POST   /api/signatures/personal-sign
POST   /api/signatures/legal-seal
GET    /api/signatures/document/{docId}
GET    /api/signatures/document/{docId}/verify
POST   /api/signatures/certificates/issue
GET    /api/signatures/certificates/{userId}
```

## Chứng thư số

- Khi startup, service tự khởi tạo Root CA nếu chưa có.
- Docker mount thư mục cert vào volume `hau_sign_certs` tại `/app/certs`.
- Cần backup volume `hau_sign_certs` nếu muốn mang dữ liệu chữ ký/certificate sang máy khác.

## Lưu ý tích hợp hiện tại

- Backend `SignRequestDto` yêu cầu `DocId` và `SignerId`.
- Frontend đã đồng bộ DTO ký số để gửi đúng `DocId`, `SignerId`, `SignerName`, `Reason`.
- SignService đọc `Documents.MinioPath` từ database dùng chung, chuẩn hóa `documents/{storedFileName}` thành object `{storedFileName}` rồi tải/lưu lại đúng file trên MinIO.
- Luồng backend ký nháy end-to-end qua Docker/Gateway đã pass với test case `TC-SIGN-001`.
- Payload theo frontend mới đã pass test case `TC-FE-SIGN-002` cho cả ký nháy và ký pháp nhân.
