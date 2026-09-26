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

- `AppUser`: user hệ thống, có `MustChangePassword` và `EmailVerifiedAt`.
- `AppRole`: vai trò.
- `AppUserRole`: bảng nối user-role.
- `Department`: cây phòng ban self-reference bằng `ParentId`.
- `PasswordResetToken`: OTP reset password, lưu hash SHA-256.
- `EmailVerificationToken`: OTP xác minh email first login, ràng buộc user + email và lưu hash SHA-256.
- `RefreshToken`: lưu hash refresh token, `AccessTokenJti`, hạn dùng, trạng thái revoke/rotate.
- `RevokedAccessToken`: blacklist JWT access token theo `jti` sau logout.

Seed data:

- 5 roles: `Admin`, `Clerk`, `Specialist`, `Manager`, `BoardOfDirectors`.
- 2 departments: HAU root và Phòng Tổng hợp.
- 1 admin user mặc định.

### Auth và onboarding

- Login trả `AccessToken`, `RefreshToken`, user info, roles, `MustChangePassword`.
- Admin tạo user mới thì user bị bắt đổi mật khẩu lần đầu.
- First login bắt buộc nhập email, gửi OTP xác minh và chỉ cập nhật email/đổi mật khẩu khi OTP hợp lệ.
- OTP xác minh email hết hạn sau 15 phút, dùng một lần; gửi lại sẽ vô hiệu hóa OTP cũ.
- Forgot password chỉ gửi OTP nếu `EmailVerifiedAt` đã có giá trị; thay đổi email sẽ đưa trạng thái về chưa xác minh.
- Forgot password gửi OTP qua email.
- Reset password xác thực OTP hash và vô hiệu hóa token đã dùng.
- Docker dev dùng Mailpit local để test email OTP an toàn, không gửi email ra internet.
- Luồng forgot/reset password backend và frontend đã pass `TC-AUTH-MAILPIT-015` bằng Mailpit.
- Refresh token đã được persist dạng SHA-256 hash trong DB và được rotate sau mỗi lần refresh.
- Refresh token cũ bị revoke, không reuse được sau khi đã rotate.
- Logout revoke toàn bộ refresh token active của user và blacklist access token hiện tại theo `jti`.
- `POST /api/auth/validate-token` trả invalid nếu access token đã nằm trong blacklist.

Lưu ý tích hợp:

- Blacklist hiện được kiểm tra trong IdentityService, endpoint `/api/auth/validate-token` và ApiGateway.
- ApiGateway validate JWT cục bộ bằng signing key trước, sau đó gọi IdentityService `/api/auth/validate-token`; vì vậy token đã logout bị chặn trên các route Document/Sign/OCR qua Gateway.
- Gateway có cấu hình `AuthValidation:FailOpenOnValidationError=true` để fallback sang JWT local nếu IdentityService validate-token tạm lỗi/timeout.

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
- Gọi IdentityService `/api/auth/validate-token` sau khi JWT local hợp lệ để chặn token đã bị logout/blacklist.
- Có fallback vận hành bằng `AuthValidation:FailOpenOnValidationError`.
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
- `DocumentAction`: `Submit`, `DeptSign`, `SubmitDirector`, `DirectorSign`, `Reject`, `Publish`, `Assign`, `UpdateOCR`.

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
POST   /api/documents/{id}/submit-director
POST   /api/documents/{id}/director-sign
POST   /api/documents/{id}/reject
POST   /api/documents/{id}/publish
POST   /api/documents/{id}/assign
```

### Workflow hiện tại

```text
Draft
  -> PendingDeptReview
  -> DeptSigned
  -> PendingDirectorSign
  -> DirectorSigned
  -> Published
```

Luồng hiện tại dùng `DeptSigned` làm trạng thái dừng riêng sau khi lãnh đạo phòng ký nháy. Manager gọi tiếp `POST /api/documents/{id}/submit-director` để trình Ban Giám hiệu ký.

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
- Luồng upload PDF thật → publish Kafka `document.uploaded` → OCRService/PaddleOCR xử lý → PATCH kết quả OCR về DocumentService đã pass test Docker/Gateway `TC-OCR-E2E-010`.

## 4. OCRService

### Backend đã có

- FastAPI app.
- PaddleOCR engine tiếng Việt.
- Chuyển PDF sang ảnh bằng `pdf2image`.
- Tải file PDF từ MinIO.
- Kafka consumer tùy chọn.
- Kafka consumer có retry loop khi Kafka chưa sẵn sàng, tránh chết thread nếu service khởi động trước broker.
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

Kiểm thử OCR đã pass:

- `TC-OCR-E2E-010`: PDF text rõ qua upload/Kafka/PaddleOCR.
- `TC-OCR-SCAN-014`: PDF dạng scan/image-based có nhiễu nhẹ và xoay nhẹ, bóc được số văn bản/ngày/title.

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
- Luồng ký số bằng role thật đã pass `TC-SIGN-ROLE-012`:
  - `Manager` ký nháy.
  - `BoardOfDirectors` ký pháp nhân.
  - Kiểm tra sai quyền trả 403 đúng kỳ vọng.
- Luồng UI ký số trực tiếp trên frontend đã pass `TC-FE-SIGN-UI-013` bằng Playwright:
  - Manager đăng nhập frontend và bấm ký nháy.
  - Board đăng nhập frontend và bấm ký pháp nhân.
  - UI verify hiển thị 2 chữ ký hợp lệ.

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

- Cấu hình mẫu Gmail SMTP đã hoàn thiện; còn test gửi thật bằng tài khoản Google và App Password hợp lệ của người triển khai.
- Nếu triển khai thực tế, bổ sung thêm bộ PDF/scan thật của nhà trường để đánh giá chất lượng OCR trên dữ liệu thật.
- Tiếp tục bổ sung test tích hợp sâu cho DocumentService/SignService/OCRService khi phát triển thêm nghiệp vụ.

## 8. Cấu hình triển khai và secrets

- Docker Compose đã hỗ trợ file `.env` ở root project.
- Repo có `.env.example` để khai báo các biến cần đổi khi deploy: PostgreSQL, MinIO, JWT, OCR service-token, SMTP/email.
- `docker-compose.yml` dùng cú pháp `${VAR:-default_dev}` để local/dev vẫn chạy nếu chưa tạo `.env`.
- Docker Compose có Mailpit cho SMTP local: SMTP `1025`, Web UI/API `8025`.
- `.gitignore` và `.dockerignore` đã bỏ qua `.env`/`.env.*`, nhưng vẫn cho phép commit `.env.example`.
- `OCRService/.env.example` chỉ dùng khi chạy OCRService độc lập ngoài Docker Compose root.
