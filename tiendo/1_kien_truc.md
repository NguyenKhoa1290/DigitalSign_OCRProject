# 🏗️ Kiến Trúc Dự Án — HAU DigitalSign OCR

> **Dự án:** Hệ thống Quản lý Công văn Số hóa & OCR
> **Trường:** Đại học Kiến Trúc Hà Nội (HAU)
> **Công nghệ:** .NET 9 Microservices + Blazor WebAssembly

---

## 📐 Tổng Quan Kiến Trúc

```
┌─────────────────────────────────────────────────────────────────┐
│                        FRONTEND                                  │
│              Blazor WebAssembly (Port 5173 / static)            │
└────────────────────────┬────────────────────────────────────────┘
                         │ HTTP/REST
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                      API GATEWAY                                 │
│               YARP Reverse Proxy (Port 5000)                    │
│  JWT Validation | Rate Limiting | Route Forwarding              │
└──────┬──────────────┬──────────────┬──────────────┬────────────┘
       │              │              │              │
  IdentityService  DocumentService  SignService   OCRService
  :5048            :5049            :5050         :5051
       │              │              │              │
       └──────────────┴──────────────┴──────────────┘
                         │
                    PostgreSQL DB
                  192.168.50.10:5432
                   DigitalSign_OCR
```

---

## 🗂️ Cấu Trúc Thư Mục

```
F:\DigitalSign_OCRProject\
├── ApiGateway\                     YARP API Gateway
├── IdentityService\
│   └── src\
│       ├── IdentityService.API\           Controllers, Middleware
│       ├── IdentityService.Core\          Entities, Interfaces, DTOs
│       └── IdentityService.Infrastructure\ DbContext, Repositories, Services
├── DocumentService\                Quản lý Công văn
├── SignService\                    Chữ ký số PKI
├── OCRService\                     Nhận dạng ký tự quang học
└── Frontend\                       Blazor WebAssembly
    ├── Auth\                       CustomAuthStateProvider
    ├── Layout\                     MainLayout, EmptyLayout, Sidebar
    ├── Models\                     DTOs phía frontend
    ├── Pages\
    │   ├── Login.razor
    │   ├── FirstLogin.razor        Đổi mật khẩu lần đầu (bắt buộc)
    │   ├── ForgotPassword.razor    Quên mật khẩu
    │   ├── ResetPassword.razor     Nhập OTP đặt lại mật khẩu
    │   ├── Dashboard.razor
    │   ├── Admin\
    │   │   ├── Users.razor         Quản lý người dùng
    │   │   ├── Departments.razor   Quản lý phòng ban (cây phân cấp)
    │   │   └── Certificates.razor  Chứng chỉ số
    │   ├── Documents\              Quản lý công văn
    │   └── Signatures\             Ký số
    ├── Shared\                      ConfirmDialog, LoadingSpinner, components dùng chung
    └── Services\
        ├── ApiService.cs           Base HTTP client (SmartDeserialize)
        ├── AuthService.cs          Auth flow
        ├── AdminService.cs         Admin CRUD
        ├── DocumentService.cs
        └── SignatureService.cs
```

---

## 🔧 Chi Tiết Từng Service

### 1. API Gateway (Port 5000)

| Thành phần | Mô tả |
|---|---|
| Framework | ASP.NET Core + YARP Reverse Proxy |
| Xác thực | JWT Validation (HS256) trước khi forward |
| Rate Limiting | 120 req/min; 10 req/min cho POST /api/auth/login |

**Bảng định tuyến:**

| Path | Service | Auth |
|---|---|---|
| /api/auth/** | IdentityService:5048 | Anonymous |
| /api/users/** | IdentityService:5048 | JWT Required |
| /api/roles/** | IdentityService:5048 | JWT Required |
| /api/departments/** | IdentityService:5048 | JWT Required |
| /api/documents/** | DocumentService:5049 | JWT Required |
| /api/signatures/** | SignService:5050 | JWT Required |
| /api/ocr/** | OCRService:5051 | JWT Required |

---

### 2. Identity Service (Port 5048) — Clean Architecture

**Core Layer** (không phụ thuộc gì):

Entities: AppUser, AppRole, AppUserRole, Department, PasswordResetToken

AppUser: Id, Username (UNIQUE), PasswordHash, FullName, Email (UNIQUE),
         PhoneNumber, DepartmentId (FK), IsActive, MustChangePassword, CreatedAt

Department: Id, DeptName, DeptCode (UNIQUE), ParentId (FK self-ref - cây phân cấp),
            Description, CreatedAt

PasswordResetToken: Id, UserId (FK), TokenHash (SHA-256), ExpiresAt, IsUsed, CreatedAt

**Infrastructure Layer**:

- AppDbContext (EF Core + Npgsql)
- UserRepository, RoleRepository, DepartmentRepository, PasswordResetRepository
- AuthService: Login, RefreshToken, ValidateToken, ChangePassword, ForgotPassword, ResetPassword
- TokenService: JWT HS256 generate/validate
- EmailService: Gmail SMTP via MailKit (OTP email)
- DepartmentService: + circular reference prevention

**API Layer**:

- AuthController: /api/auth/(login|logout|refresh-token|validate-token|change-password|forgot-password|reset-password)
- UsersController: CRUD + /api/users/{id}/roles/{roleId}
- RolesController: GET /api/roles
- DepartmentsController: CRUD + /api/departments/tree
- ExceptionHandlingMiddleware: global error → HTTP status mapping

**Roles được seed sẵn:**

| RoleName | Mô tả |
|---|---|
| Admin | Quản trị viên hệ thống |
| Clerk | Văn thư |
| Specialist | Chuyên viên |
| Manager | Lãnh đạo Phòng |
| BoardOfDirectors | Ban Giám hiệu |

---

### 3. Frontend — Blazor WebAssembly

**Tech stack:** .NET 9, Blazored.LocalStorage, Blazored.Toast, CSS Design System riêng

**Luồng xác thực:**

```
Login
  ├── MustChangePassword=true  → /first-login → đổi mật khẩu → /
  └── MustChangePassword=false → /

Quên mật khẩu:
  /forgot-password (nhập email → gửi OTP)
       ↓
  /reset-password (nhập OTP 6 số + mật khẩu mới)
       ↓
  /login
```

JWT lưu trong localStorage. CustomAuthStateProvider parse JWT claims.
ApiService.SmartDeserialize() tự động unwrap ApiResponse<T> wrapper nếu cần.

---


---

### 4. OCR Service (Port 5051) — Python / FastAPI

**Tech stack:** Python, FastAPI, PaddleOCR, pdf2image, MinIO, Kafka

**Cau truc:**

`
OCRService\
├── .env / .env.example       Bien moi truong (Kafka, MinIO, port...)
├── requirements.txt          Python dependencies
├── README.md
└── app\
    ├── main.py               FastAPI entry point + Kafka consumer startup
    ├── config.py             Settings (pydantic BaseSettings)
    ├── api\
    │   └── routes.py         3 endpoints: /process, /process-upload, /health
    ├── ocr\
    │   ├── engine.py         OcrEngine — wrapper PaddleOCR tieng Viet
    │   └── extractor.py      Boc tach cac truong: so hieu, ngay, trich yeu, co quan
    └── services\
        ├── ocr_processor.py    Xu ly toan bo luong: MinIO -> OCR -> DocumentService
        ├── minio_service.py    Tai file PDF tu MinIO
        ├── kafka_consumer.py   Kafka consumer (background thread)
        └── document_service.py Goi lai DocumentService cap nhat ket qua OCR
`

**API Endpoints:**

| Method | Path | Mo ta |
|---|---|---|
| POST | /api/ocr/process | Kich hoat OCR theo doc_id + minio_path |
| POST | /api/ocr/process-upload | Upload PDF va chay OCR truc tiep (test) |
| GET | /api/ocr/health | Health check |

**Luong xu ly tu dong (Kafka):**

`
DocumentService upload file
    -> Publish Kafka event { doc_id, minio_path, token }
    -> OCRService (Kafka consumer)
    -> Tai PDF tu MinIO
    -> Chuyen PDF sang anh (pdf2image, DPI=200)
    -> PaddleOCR (tieng Viet, angle classifier)
    -> Boc tach cac truong: so hieu, ngay ban hanh, trich yeu, co quan
    -> Goi DocumentService cap nhat ket qua
`

**Luong thu cong (REST):**

`
POST /api/ocr/process { doc_id, minio_path, token }
    -> Tai PDF tu MinIO -> OCR -> tra ket qua + cap nhat DocumentService
`

**Boc tach thong tin cong van (extractor.py):**

| Truong | Regex / Logic |
|---|---|
| so_hieu | Pattern: 123/QD-HAU, 456/CV-CNTT (regex) |
| ngay_ban_hanh | Pattern: DD/MM/YYYY hoac "ngay X thang Y nam Z" |
| trich_yeu | Sau cac tu khoa: V/v, Ve viec, Trich yeu, Kinh gui |
| co_quan | 10 dong dau trang, chua tu khoa: truong, phong, khoa, ban... |

**Trang thai Frontend:** Chua co — chi co backend. Frontend se can bo sung trang xem ket qua OCR.

## 🔐 Bảo Mật

| Lớp | Cơ chế |
|---|---|
| Mật khẩu | BCrypt work factor 12 |
| Token | JWT HS256, expire 60 phút |
| OTP reset | SHA-256 hash lưu DB, expire 15 phút, single-use |
| Rate Limit | 10 req/min cho login |
| Vòng lặp phòng ban | HasCircularReferenceAsync() kiểm tra trước khi update ParentId |

---

## ⚙️ Cấu Hình Môi Trường

| Mục | Giá trị |
|---|---|
| DB Server | 192.168.50.10:5432 |
| DB Name | DigitalSign_OCR |
| JWT Issuer | IdentityService |
| JWT Audience | HAU-MicroservicesClients |
| SMTP | Gmail smtp.gmail.com:587 (TLS) |
| Admin mặc định | username: admin, MustChangePassword: false |
