# Identity Service - Hệ thống Quản lý Công văn HAU

Dịch vụ xác thực và phân quyền (Identity Service) cho Hệ thống Thông tin Hỗ trợ Quản lý Công văn Đi và Đến của Trường Đại học Kiến Trúc Hà Nội.

## 🏗 Kiến trúc

```
IdentityService/
├── src/
│   ├── IdentityService.API/           # ASP.NET Core Web API (controllers, middleware)
│   ├── IdentityService.Core/          # Domain entities, interfaces, DTOs
│   └── IdentityService.Infrastructure/# EF Core, JWT, BCrypt implementations
├── tests/
│   └── IdentityService.Tests/         # Unit & Integration tests
├── Dockerfile                         # Multi-stage Docker build
├── docker-compose.yml                 # PostgreSQL + API stack
└── IdentityService.sln
```

## 🚀 Chạy nhanh

### Với Docker (khuyến nghị)

```bash
cd IdentityService
docker-compose up --build
```

API sẽ khởi động tại: http://localhost:5001  
Swagger UI: http://localhost:5001 (trang chủ)

### Chạy cục bộ (local)

```bash
# Cần PostgreSQL đang chạy tại localhost:5432
cd IdentityService
dotnet restore
dotnet run --project src/IdentityService.API
```

## 📋 API Endpoints

### 🔐 Authentication (`/api/v1/auth`)

| Method | Endpoint | Mô tả | Auth |
|--------|----------|-------|------|
| POST | `/login` | Đăng nhập, nhận JWT Token | Public |
| POST | `/refresh-token` | Làm mới Access Token | Public |
| POST | `/validate-token` | Kiểm tra Token (dùng bởi Gateway) | Public |
| POST | `/logout` | Đăng xuất | Bearer Token |

### 👥 Users (`/api/v1/users`)

| Method | Endpoint | Mô tả | Role |
|--------|----------|-------|------|
| GET | `/` | Danh sách users (có phân trang) | Admin |
| GET | `/{id}` | Lấy user theo ID | Authenticated |
| GET | `/me` | Thông tin user hiện tại | Authenticated |
| POST | `/` | Tạo tài khoản mới | Admin |
| PUT | `/{id}` | Cập nhật thông tin | Admin hoặc chính user |
| DELETE | `/{id}` | Xóa tài khoản | Admin |
| POST | `/{id}/roles/{roleId}` | Gán vai trò | Admin |
| DELETE | `/{id}/roles/{roleId}` | Thu hồi vai trò | Admin |

### 🏢 Departments (`/api/v1/departments`)

| Method | Endpoint | Mô tả | Role |
|--------|----------|-------|------|
| GET | `/` | Danh sách tất cả đơn vị | Authenticated |
| GET | `/{id}` | Lấy đơn vị theo ID | Authenticated |
| GET | `/{id}/children` | Đơn vị con | Authenticated |
| POST | `/` | Tạo đơn vị mới | Admin |
| PUT | `/{id}` | Cập nhật đơn vị | Admin |
| DELETE | `/{id}` | Xóa đơn vị | Admin |

### 🎭 Roles (`/api/v1/roles`)

| Method | Endpoint | Mô tả | Role |
|--------|----------|-------|------|
| GET | `/` | Danh sách vai trò | Authenticated |
| GET | `/{id}` | Lấy vai trò theo ID | Authenticated |

## 🔑 Tài khoản mặc định

| Username | Password | Role |
|----------|----------|------|
| `admin` | `Admin@123456` | Admin |

## 🎭 Danh sách Vai trò (RBAC)

| Role | Tiếng Việt |
|------|-----------|
| `Admin` | Quản trị viên |
| `Clerk` | Văn thư |
| `Specialist` | Chuyên viên |
| `Manager` | Lãnh đạo Phòng |
| `BoardOfDirectors` | Ban Giám hiệu |

## 🧪 Chạy Tests

```bash
cd IdentityService
dotnet test --verbosity normal
```

## ⚙️ Cấu hình JWT

Trong `appsettings.json`:

```json
{
  "JwtSettings": {
    "Key": "your-secret-key-min-32-chars",
    "Issuer": "IdentityService",
    "Audience": "HAU-MicroservicesClients",
    "ExpiryMinutes": 60
  }
}
```

## 🐳 Environment Variables (Docker)

| Biến | Mô tả |
|------|-------|
| `ConnectionStrings__DefaultConnection` | Connection string PostgreSQL |
| `JwtSettings__Key` | Khóa bí mật JWT |
| `JwtSettings__Issuer` | Issuer của JWT |
| `JwtSettings__Audience` | Audience của JWT |
| `JwtSettings__ExpiryMinutes` | Thời gian hết hạn token (phút) |
