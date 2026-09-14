# 🏛️ Hệ thống Quản lý Công văn - Trường ĐH Kiến Trúc Hà Nội
> Microservices Architecture | ASP.NET Core .NET 9 | PostgreSQL | MinIO | Kafka | Ocelot

---

## 📐 Kiến trúc tổng thể

```
Client (Web/Mobile)
        │
        ▼
  ┌─────────────┐
  │ API Gateway │  ← Ocelot / YARP
  │  :8080      │  (Routing, Auth, Rate Limit, Load Balance)
  └──────┬──────┘
         │
    ┌────┴────────────────────────┐
    │                             │
    ▼                             ▼
┌──────────────┐          ┌──────────────────┐
│IdentityService│         │ DocumentService   │  ← REST (sync)
│   :5048      │          │   :5049          │
└──────────────┘          └──────┬───────────┘
                                 │
                                 ▼ publish event
                          ┌─────────────┐
                          │    Kafka    │  ← Message Broker (async)
                          └──────┬──────┘
                                 │ subscribe
                   ┌─────────────┴──────────────┐
                   ▼                             ▼
            ┌────────────┐             ┌──────────────────┐
            │OCR Service │             │Notification      │
            │   :5050    │             │Service   :5051   │
            └────────────┘             └──────────────────┘
```

---

## 🗂️ Lộ trình phát triển

| Giai đoạn | Service | Trạng thái |
|---|---|---|
| Tuần 1 | **Identity Service** | ✅ HOÀN THÀNH |
| Tuần 2 | **Document Service** | 🔄 TIẾP THEO |
| Tuần 3 | **OCR Service** | ⏳ Chờ |
| Tuần 4 | **API Gateway + Kafka** | ⏳ Chờ |

---

## ✅ Identity Service (HOÀN THÀNH)

### Thông tin kết nối
| Thuộc tính | Giá trị |
|---|---|
| URL | `http://localhost:5048` |
| Swagger | `http://localhost:5048` (root) |
| Database | PostgreSQL @ `192.168.50.10:5432` |
| Database Name | `DigitalSign_OCR` |
| User DB | `postgres` / `1111` |

### Tài khoản mặc định
| Thuộc tính | Giá trị |
|---|---|
| Username | `admin` |
| Password | `Admin@123` |
| Role | `Admin` |

### Endpoints chính
```
POST   /api/v1/auth/login           Đăng nhập, trả JWT
POST   /api/v1/auth/refresh-token   Làm mới token
POST   /api/v1/auth/validate-token  Xác thực token
POST   /api/v1/auth/logout          Đăng xuất

GET    /api/v1/users                Danh sách user (cần JWT)
POST   /api/v1/users                Tạo user mới
GET    /api/v1/users/{id}           Chi tiết user
PUT    /api/v1/users/{id}           Cập nhật user
DELETE /api/v1/users/{id}           Xóa user
GET    /api/v1/users/me             Thông tin user hiện tại

GET    /api/v1/roles                Danh sách roles
GET    /api/v1/departments          Danh sách phòng ban
GET    /health                      Health check
```

### Roles hệ thống
| Role | Mô tả |
|---|---|
| `Admin` | Quản trị viên, toàn quyền |
| `Clerk` | Chuyên viên văn thư |
| `Specialist` | Chuyên viên nghiệp vụ |
| `Manager` | Trưởng/Phó phòng |
| `BoardOfDirectors` | Ban Giám hiệu |

### Cấu trúc project
```
IdentityService/
├── src/
│   ├── IdentityService.Core/           # Entities, Interfaces, DTOs, Exceptions
│   ├── IdentityService.Infrastructure/ # EF Core, Repositories, Services
│   └── IdentityService.API/            # Controllers, Middleware, Program.cs
└── tests/
    └── IdentityService.Tests/          # Unit + Integration Tests
```

### Chạy service
```bash
cd IdentityService
dotnet run --project src/IdentityService.API
```

---

## ✅ Document Service (HOÀN THÀNH)

### Thông tin kết nối
| Thuộc tính | Giá trị |
|---|---|
| URL | `http://localhost:5049` |
| Swagger | `http://localhost:5049` |
| Database | PostgreSQL @ `192.168.50.10:5432` (cùng DB `DigitalSign_OCR`) |
| File Storage | MinIO @ `192.168.50.10:9000` (S3 API) |
| MinIO Console | `http://192.168.50.10:9001` |
| MinIO Credentials | `minioadmin` / `minioadmin` |
| MinIO Bucket | `documents` |

### Endpoints chính
```
GET    /api/documents                        Danh sách công văn (phân trang, lọc)
POST   /api/documents                        Tạo công văn mới (Draft)
GET    /api/documents/{id}                   Chi tiết công văn
GET    /api/documents/code/{code}            Tìm theo mã công văn
PUT    /api/documents/{id}                   Cập nhật (chỉ khi Draft)
DELETE /api/documents/{id}                   Xóa mềm (chỉ khi Draft/Rejected)

POST   /api/documents/{id}/submit            Gửi chờ duyệt (Draft → PendingReview)
POST   /api/documents/{id}/approve           Phê duyệt (PendingReview → Approved)
POST   /api/documents/{id}/reject            Từ chối (PendingReview → Rejected)
POST   /api/documents/{id}/publish           Phát hành (Approved → Published)

GET    /api/documents/{id}/attachments       Danh sách file đính kèm
POST   /api/documents/{id}/attachments       Upload file (max 50MB)
GET    /api/documents/{id}/attachments/{aid}/download      Tải file
GET    /api/documents/{id}/attachments/{aid}/presigned-url URL tạm thời (1h)
DELETE /api/documents/{id}/attachments/{aid}               Xóa file
```

### Workflow công văn
```
Draft → PendingReview → Approved → Published
                    ↘ Rejected
```

### Chạy service
```bash
cd DocumentService
dotnet run --project src/DocumentService.API
```


---

## 🔧 API Gateway (Ocelot)

### Chức năng
| Chức năng | Mô tả |
|---|---|
| **Routing** | `/api/identity/*` → `:5048`, `/api/documents/*` → `:5049` |
| **Authentication** | Validate JWT một lần, forward thông tin user |
| **Rate Limiting** | Giới hạn request/phút per user |
| **Load Balancing** | Phân tải khi scale nhiều instance |

---

## 📨 Kafka (Message Broker)

### Khi nào dùng Kafka
| Pattern | Ví dụ |
|---|---|
| **Async events** | Upload xong → OCR tự động xử lý |
| **Decoupling** | DocumentService không cần biết OCR Service |
| **Fan-out** | 1 event → nhiều service nhận cùng lúc |
| **Reliability** | Message không mất dù service tạm restart |

### Topics dự kiến
| Topic | Producer | Consumer |
|---|---|---|
| `document.uploaded` | DocumentService | OCR Service, Notification Service |
| `document.ocr.completed` | OCR Service | DocumentService, Notification Service |
| `user.created` | IdentityService | Notification Service |

---

## 🛠️ Tech Stack

| Thành phần | Công nghệ |
|---|---|
| Framework | ASP.NET Core `.NET 9` |
| ORM | Entity Framework Core + Npgsql |
| Database | PostgreSQL `9.0.1` |
| Auth | JWT Bearer + BCrypt |
| File Storage | MinIO |
| Message Broker | Apache Kafka |
| API Gateway | Ocelot |
| Logging | Serilog |
| Testing | xUnit + Moq + FluentAssertions |
| API Docs | Swagger / Swashbuckle |

---

## 📝 Ghi chú cấu hình

- **Connection String:** `Host=192.168.50.10;Port=5432;Database=DigitalSign_OCR;Username=postgres;Password=1111;SSL Mode=Disable`
- **JWT Key:** `HAU-IdentityService-SuperSecret-Key-2024-MustBe32CharsOrMore!!`
- **JWT Issuer:** `IdentityService`
- **JWT Audience:** `HAU-MicroservicesClients`
- **Token expiry:** 60 phút | Refresh token: 7 ngày
