# Identity Service

Dịch vụ xác thực và phân quyền cho hệ thống HAU DigitalSign OCR.

## Kiến trúc

```text
IdentityService/
├── src/
│   ├── IdentityService.API/             Controllers, middleware, Program.cs
│   ├── IdentityService.Core/            Entities, DTOs, interfaces, exceptions
│   └── IdentityService.Infrastructure/  EF Core, repositories, services
├── tests/
│   └── IdentityService.Tests/
├── Dockerfile
├── docker-compose.yml
└── IdentityService.slnx
```

## Chạy local

```bash
cd IdentityService
dotnet restore
dotnet run --project src/IdentityService.API
```

Port dev theo `launchSettings.json`:

```text
http://localhost:5048
```

Swagger UI chạy ở root khi môi trường là Development/Docker:

```text
http://localhost:5048
```

## Chạy bằng Docker

Khuyến nghị chạy từ root repository bằng compose full stack:

```bash
docker compose up -d identity-service
```

Hoặc chạy toàn bộ hệ thống:

```bash
docker compose up -d
```

File `IdentityService/docker-compose.yml` là cấu hình độc lập/legacy chỉ cho IdentityService + PostgreSQL riêng, không đại diện cho cấu hình tích hợp qua Gateway hiện tại.

Nếu vẫn muốn chạy cấu hình độc lập này:

```bash
cd IdentityService
docker-compose up --build
```

Lưu ý: kiểm tra lại environment variables trước khi chạy, vì cấu hình local trong repo có thể khác cấu hình qua Gateway.

## API endpoints

Route hiện tại là `/api/...`, không dùng `/api/v1/...`.

### Authentication - `/api/auth`

| Method | Endpoint | Mô tả | Auth |
|---|---|---|---|
| POST | `/login` | Đăng nhập, nhận JWT và refresh token | Public |
| POST | `/refresh-token` | Làm mới access token, rotate refresh token | Public |
| POST | `/validate-token` | Kiểm tra token | Public |
| POST | `/logout` | Đăng xuất, revoke refresh token và blacklist access token hiện tại | Bearer |
| POST | `/change-password` | Đổi mật khẩu, dùng cho first login | Bearer |
| POST | `/forgot-password` | Gửi OTP reset password qua email | Public |
| POST | `/reset-password` | Đặt lại mật khẩu bằng OTP | Public |

## Refresh token và logout

- Refresh token không lưu plain text; hệ thống lưu SHA-256 hash trong bảng `RefreshTokens`.
- Mỗi lần gọi `/api/auth/refresh-token` thành công, refresh token cũ bị revoke và token mới được tạo.
- `/api/auth/logout` revoke toàn bộ refresh token active của user và lưu `jti` access token vào bảng `RevokedAccessTokens`.
- `/api/auth/validate-token` trả invalid nếu access token đã nằm trong blacklist.
- Lưu ý: ApiGateway hiện validate JWT cục bộ bằng signing key, chưa gọi blacklist IdentityService cho từng request tới Document/Sign/OCR.

## Email reset password

IdentityService gửi OTP reset password bằng MailKit/MimeKit `4.18.0`.

Cấu hình nằm trong section `EmailSettings`:

```json
{
  "EmailSettings": {
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": "587",
    "Username": "<smtp-user>",
    "Password": "<smtp-app-password>",
    "FromName": "HAU Documents"
  }
}
```

`Username` và `Password` là bắt buộc khi gửi mail. Không commit credential thật vào repo; khi triển khai nên đưa qua environment variables hoặc secret manager.

### Users - `/api/users`

| Method | Endpoint | Mô tả |
|---|---|---|
| GET | `/` | Danh sách user, phân trang/tìm kiếm |
| GET | `/{id}` | Lấy user theo ID |
| GET | `/me` | Thông tin user hiện tại |
| POST | `/` | Tạo user |
| PUT | `/{id}` | Cập nhật user |
| DELETE | `/{id}` | Xóa user |
| POST | `/{id}/roles/{roleId}` | Gán role |
| DELETE | `/{id}/roles/{roleId}` | Gỡ role |

### Departments - `/api/departments`

| Method | Endpoint | Mô tả |
|---|---|---|
| GET | `/` | Danh sách phòng ban dạng flat list |
| GET | `/tree` | Cây phòng ban |
| GET | `/{id}` | Lấy phòng ban theo ID |
| GET | `/{id}/children` | Lấy phòng ban con |
| POST | `/` | Tạo phòng ban |
| PUT | `/{id}` | Cập nhật phòng ban |
| DELETE | `/{id}` | Xóa phòng ban |

### Roles - `/api/roles`

| Method | Endpoint | Mô tả |
|---|---|---|
| GET | `/` | Danh sách role |
| GET | `/{id}` | Lấy role theo ID |

## Roles

| Role | Ý nghĩa |
|---|---|
| `Admin` | Quản trị viên |
| `Clerk` | Văn thư |
| `Specialist` | Chuyên viên |
| `Manager` | Lãnh đạo phòng |
| `BoardOfDirectors` | Ban Giám hiệu |

## Tài khoản seed

| Username | Password seed | Role |
|---|---|---|
| `admin` | `Admin@123` | Admin |

## Database

IdentityService hiện dùng:

```csharp
await context.Database.EnsureCreatedAsync();
```

Điều này nghĩa là:

- Lần đầu chạy sẽ tạo schema và seed data.
- Khi entity thay đổi sau khi DB đã tồn tại, schema không tự cập nhật.
- Nếu thêm cột/bảng mới, cần SQL thủ công hoặc chuyển sang EF migrations.

Các bảng chính:

- `AppUsers`
- `AppRoles`
- `AppUserRoles`
- `Departments`
- `PasswordResetTokens`

## JWT

Các service khác validate JWT theo cấu hình:

```json
{
  "JwtSettings": {
    "Issuer": "IdentityService",
    "Audience": "HAU-MicroservicesClients"
  }
}
```

## Test

```bash
cd IdentityService
dotnet test
```
