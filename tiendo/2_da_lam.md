# Những Gì Đã Làm

> Cập nhật theo code hiện tại trong repository.

## 1. IdentityService

### Kiến trúc

- Tách 3 layer: `IdentityService.Core`, `IdentityService.Infrastructure`, `IdentityService.API`.
- Dùng EF Core 9 + Npgsql.
- Dùng JWT Bearer HS256.
- Dùng BCrypt work factor 12 cho mật khẩu.
- Có global exception middleware.

### Entity và dữ liệu

- `AppUser`: user hệ thống, có `MustChangePassword`.
- `AppRole`: vai trò.
- `AppUserRole`: bảng nối user-role.
- `Department`: cây phòng ban self-reference bằng `ParentId`.
- `PasswordResetToken`: OTP reset password, lưu hash SHA-256.

Seed data:

- 5 roles: `Admin`, `Clerk`, `Specialist`, `Manager`, `BoardOfDirectors`.
- 2 departments: HAU root và Phòng Tổng hợp.
- 1 admin user mặc định.

### Auth và onboarding

- Login trả `AccessToken`, `RefreshToken`, user info, roles, `MustChangePassword`.
- Admin tạo user mới thì user bị bắt đổi mật khẩu lần đầu.
- User đổi mật khẩu có thể cập nhật email/SĐT.
- Forgot password gửi OTP qua email.
- Reset password xác thực OTP hash và vô hiệu hóa token đã dùng.
- Logout hiện vẫn là stub, chưa có JWT blacklist.
- Refresh token hiện sinh mới nhưng chưa persist DB.

### User, role, department

- CRUD user.
- Gán/gỡ role cho user.
- CRUD department.
- Lấy cây department bằng `/api/departments/tree`.
- Có chống vòng lặp khi đổi `ParentId`.

## 2. ApiGateway

- Dùng ASP.NET Core + YARP Reverse Proxy.
- Port dev: `5000`.
- Validate JWT tại gateway cho các route cần auth.
- Cho anonymous với `/api/auth/**`.
- Có rate limit:
  - Mặc định 120 request/phút.
  - Login 10 request/phút.
- Route đến:
  - IdentityService `:5048`
  - DocumentService `:5049`
  - SignService `:5050`
  - OCRService `:5051`

## 3. DocumentService

### Kiến trúc

- Tách 3 layer: `DocumentService.Core`, `DocumentService.Infrastructure`, `DocumentService.API`.
- Dùng EF Core migrations và tự `MigrateAsync()` khi startup.
- Dùng JWT Bearer để validate token do IdentityService phát hành.
- Dùng MinIO để lưu PDF.
- Có Kafka producer cho event OCR.

### Entity và DTO

- `Document`: metadata văn bản, `DocNumber`, `Title`, `IssuedDate`, `MinioPath`, `OcrDataRaw`, `Status`, `DocTypeId`.
- `DocumentType`: loại văn bản.
- `DocumentProcess`: lịch sử xử lý văn bản.
- `DocumentStatus`: `Draft`, `PendingDeptReview`, `DeptSigned`, `PendingDirectorSign`, `DirectorSigned`, `Published`, `Rejected`.
- `DocumentAction`: `Submit`, `DeptSign`, `DirectorSign`, `Reject`, `Publish`, `Assign`, `UpdateOCR`.

### API đã có

```text
GET    /api/documents
POST   /api/documents
GET    /api/documents/types
GET    /api/documents/{id}
DELETE /api/documents/{id}
POST   /api/documents/{id}/upload
PATCH  /api/documents/{id}/ocr
POST   /api/documents/{id}/submit
POST   /api/documents/{id}/dept-sign
POST   /api/documents/{id}/director-sign
POST   /api/documents/{id}/reject
POST   /api/documents/{id}/publish
POST   /api/documents/{id}/assign
```

### Workflow hiện tại

```text
Draft
  -> PendingDeptReview
  -> DeptSigned (đã khai báo trong code)
  -> PendingDirectorSign
  -> DirectorSigned
  -> Published
```

Lưu ý: implementation hiện tại của `DeptSignAsync` đang chuyển thẳng từ `PendingDeptReview` sang `PendingDirectorSign`; `DeptSigned` tồn tại trong status constants nhưng chưa được dùng như trạng thái dừng riêng.

Reject được cho phép ở các trạng thái pending:

```text
PendingDeptReview / DeptSigned / PendingDirectorSign -> Rejected
```

### Upload và OCR

- Upload file lên MinIO bucket `documents`.
- File được lưu bằng tên GUID ngẫu nhiên, `MinioPath = "documents/{storedFileName}"`.
- Sau upload, service publish Kafka event `document.uploaded` nếu Kafka bật.
- OCR result cập nhật bằng `PATCH /api/documents/{id}/ocr`.

Tình trạng hiện tại:

- Kafka event vẫn gửi `authToken` rỗng, nhưng DocumentService đã hỗ trợ header nội bộ `X-Service-Token`.
- OCRService đã fallback sang `SERVICE_TOKEN` khi Kafka event không có JWT, nên có thể tự PATCH kết quả OCR về DocumentService trong Docker/local.

## 4. OCRService

### Backend đã có

- FastAPI app.
- PaddleOCR engine tiếng Việt.
- Chuyển PDF sang ảnh bằng `pdf2image`.
- Tải file PDF từ MinIO.
- Kafka consumer tùy chọn.
- Gọi lại DocumentService để cập nhật OCR result.

### API đã có

```text
POST /api/ocr/process
POST /api/ocr/process-upload
GET  /api/ocr/health
```

### Trường bóc tách

- `doc_number`
- `issued_date`
- `title`
- `issuing_org`
- `ocr_data_raw`

Tình trạng frontend:

- Đã có màn hình riêng `/documents/{id}/ocr` để xem/kiểm tra kết quả OCR.
- Màn hình đọc `OcrDataRaw`, hiển thị trường bóc tách, dòng text nhận diện, raw JSON và lịch sử `UpdateOCR`.

## 5. SignService

### Backend đã có

- Tách 3 layer: `SignService.Core`, `SignService.Infrastructure`, `SignService.API`.
- Dùng EF Core migrations và tự `MigrateAsync()` khi startup.
- Dùng iText7 + BouncyCastle để ký PDF.
- Tạo Root CA nội bộ nếu chưa có.
- Cấp certificate cho user.
- Lưu metadata chữ ký vào PostgreSQL.
- Dùng MinIO để tải/lưu PDF đã ký.
- Đọc `Documents.MinioPath` từ database dùng chung để tải đúng object PDF do DocumentService upload.

### API đã có

```text
POST /api/signatures/personal-sign
POST /api/signatures/legal-seal
GET  /api/signatures/document/{docId}
GET  /api/signatures/document/{docId}/verify
POST /api/signatures/certificates/issue
GET  /api/signatures/certificates/{userId}
```

### Rule ký

- `personal-sign`: role `Manager` hoặc `Admin`.
- `legal-seal`: role `BoardOfDirectors` hoặc `Admin`.
- `legal-seal` yêu cầu văn bản đã có `PersonalSignature`.
- Mỗi document chỉ có một chữ ký mỗi loại.

Điểm cần hoàn thiện:

- Luồng UI ký số nên tiếp tục được kiểm thử thủ công trên trình duyệt với nhiều role thật (`Manager`, `BoardOfDirectors`) khi có dữ liệu người dùng tương ứng.

## 6. Frontend

### Đã có

- Blazor WebAssembly .NET 9.
- Base API trỏ đến Gateway `http://localhost:5000/`.
- JWT lưu trong localStorage.
- `CustomAuthStateProvider` parse JWT và kiểm tra expiry.
- `ApiService.SmartDeserialize()` unwrap được response có dạng `ApiResponse<T>`.
- Frontend ký số đã gửi đúng DTO backend: `DocId`, `SignerId`, `SignerName`, `Reason`; map `DeptSign -> personal-sign`, `DirectorSign -> legal-seal`.

### Màn hình chính

- Login.
- First login/change password.
- Forgot password.
- Reset password.
- Dashboard.
- Admin Users.
- Admin Departments.
- Admin Certificates.
- Documents list/create/detail.
- Documents OCR result.
- Signatures page.

## 7. Những việc còn lại

- Persist refresh token và/hoặc thêm JWT blacklist khi logout.
- Rà soát secrets trong `appsettings*.json` trước khi deploy.
- Bổ sung test cho DocumentService, SignService và flow tích hợp.
