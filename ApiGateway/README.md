# API Gateway

Gateway trung tâm của hệ thống HAU DigitalSign OCR. Gateway dùng ASP.NET Core + YARP để định tuyến request đến các service phía sau, validate JWT cho các route cần đăng nhập và kiểm tra blacklist token qua IdentityService.

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
| `/api/admin/system-logs`, `/api/admin/activity` | Gateway controllers | Admin |
| `/api/notifications`, `/api/notifications/{id}/read`, `/api/notifications/read-all` | Gateway controllers | JWT, chỉ user hiện tại |

## Cấu hình quan trọng

- `ConnectionStrings:DefaultConnection` (`ConnectionStrings__DefaultConnection` trong Docker) trỏ PostgreSQL dùng chung. Cần Identity/Document đã tạo bảng trước khi chạy Gateway; SQL initializer transaction tạo bảng/index/trigger log/audit/thông báo, retry 10 lần trước khi phục vụ. PostgreSQL volume giữ dữ liệu khi recreate container.
- Nhật ký hệ thống lưu HTTP qua Gateway và startup; audit công văn từ trigger process, nghiệp vụ auth/quản trị/ký/upload/xóa từ Gateway. Không thu gom toàn bộ log nội bộ service và không backfill lịch sử.
- Thông báo workflow theo người nhận/người tạo/role đang hoạt động. API lấy user từ JWT; đọc từng bản ghi hoặc tất cả chỉ ảnh hưởng user hiện tại. Cảnh báo chứng thư còn hiệu lực/hết hạn trong 30 ngày được tạo khi đọc danh sách; frontend polling 30 giây, chưa push.
- Không lưu body/query/token/password; URL ngoài allowlist được che. Response `X-Trace-Id` liên kết HTTP với audit process theo W3C trace. API monitor/notification không tự tạo log vòng lặp, response no-store.
- Kiểm thử local: `tests/monitoring-notifications.spec.cjs`; xUnit bảo vệ route/action nằm trong `ApiGateway/tests/ApiGateway.Tests`.

- Local config trong `appsettings.json` trỏ đến `localhost:5048/5049/5050/5051`.
- Docker Compose override các destination sang hostname nội bộ: `identity-service`, `document-service`, `sign-service`, `ocr-service`.
- `JwtSettings` phải khớp với IdentityService và các downstream service.
- `AuthValidation:ValidateTokenUrl` trỏ tới IdentityService `/api/auth/validate-token`; Gateway gọi endpoint này sau khi JWT đã pass kiểm tra chữ ký/issuer/audience/expiry local để chặn token đã logout.
- Trong Docker, biến môi trường `AuthValidation__ValidateTokenUrl` đang trỏ tới `http://identity-service:8080/api/auth/validate-token`.
- `AuthValidation:FailOpenOnValidationError=true` cho phép Gateway fallback sang kết quả JWT local nếu IdentityService tạm lỗi/timeout. Token đã logout vẫn bị chặn khi IdentityService phản hồi `isValid=false`.
