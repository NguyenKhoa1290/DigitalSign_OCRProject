# API Gateway

Gateway trung tâm của hệ thống HAU DigitalSign OCR. Gateway dùng ASP.NET Core + YARP để định tuyến request đến các service phía sau và validate JWT cho các route cần đăng nhập.

## Chạy local

Từ root repository:

```bash
dotnet run --project ApiGateway/ApiGateway.csproj
```

Port mặc định:

```text
http://localhost:5000
```

Health check:

```text
GET /health
```

## Chạy bằng Docker

Khuyến nghị chạy bằng compose ở root:

```bash
docker compose up -d api-gateway
```

Hoặc chạy toàn bộ hệ thống:

```bash
docker compose up -d
```

## Route qua Gateway

| Path | Service đích | Auth |
|---|---|---|
| `/api/auth/**` | IdentityService | Anonymous |
| `/api/users/**` | IdentityService | JWT |
| `/api/roles/**` | IdentityService | JWT |
| `/api/departments/**` | IdentityService | JWT |
| `/api/documents/**` | DocumentService | JWT |
| `/api/signatures/**` | SignService | JWT |
| `/api/ocr/**` | OCRService | JWT |

## Cấu hình quan trọng

- Local config trong `appsettings.json` trỏ đến `localhost:5048/5049/5050/5051`.
- Docker Compose override các destination sang hostname nội bộ: `identity-service`, `document-service`, `sign-service`, `ocr-service`.
- `JwtSettings` phải khớp với IdentityService và các downstream service.

