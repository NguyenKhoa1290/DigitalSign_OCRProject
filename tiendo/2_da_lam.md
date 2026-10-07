# Những Gì Đã Làm

> Đối chiếu với source ngày 05/10/2026. Tích hợp công văn, thống kê Dashboard và log/audit/thông báo đã được build, triển khai Docker và kiểm thử trong Công việc số 44–46; các kết quả khác giữ ngày chạy lịch sử.

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
- `GET /api/users/stats`: Admin, tổng người dùng toàn database (bao gồm tài khoản bị khóa), không phụ thuộc phân trang.
- Danh bạ người nhận `/api/users/assignees?search=...` cho Admin/Clerk/Manager/BoardOfDirectors, chỉ trả thông tin tối thiểu của tài khoản hoạt động.
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
- Có API Admin `/api/admin/system-logs`, `/api/admin/activity`: log HTTP qua Gateway, sự kiện startup và audit nghiệp vụ; lọc service/mức độ/ngày/trace/actor, phân trang và no-store. Không lưu body/query/token/mật khẩu, URL ngoài allowlist được che.
- Có API `/api/notifications`, PATCH `/{id}/read`, POST `/read-all`: ràng buộc user từ JWT, lưu PostgreSQL; trigger process tạo audit/thông báo ngay cả khi OCR callback gọi trực tiếp DocumentService. Kiểm tra chứng thư sắp hết hạn khi đọc thông báo, chống lặp theo thumbprint.
- Gateway cần PostgreSQL và các bảng Identity/Document; initializer transaction tạo bảng/index/function/trigger, không backfill lịch sử. `DocumentProcessRepository` đặt trace trong transaction để liên kết audit với request Gateway.
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
- `DocumentAction`: `Submit`, `DeptSign`, `SubmitDirector`, `DirectorSign`, `Reject`, `Publish`, `Assign`, `UpdateOCR`, `Update`.

### API đã có

```text
GET    /api/documents
POST   /api/documents
GET    /api/documents/types
GET    /api/documents/stats
GET    /api/documents/{id}
PUT    /api/documents/{id}
GET    /api/documents/{id}/file?download=false
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

- Sửa metadata dành cho Admin/Clerk/Specialist, chỉ ở `Draft` hoặc `Rejected`; giữ nguyên file/OCR/workflow và ghi lịch sử `Update`.
- Xem/tải PDF có JWT, đọc đúng object theo `Documents.MinioPath`; trả 404 khi không có file, hỗ trợ HTTP range và attachment khi `download=true`.
- Phân công dành cho Admin/Clerk/Manager/BoardOfDirectors; xác nhận người nhận tồn tại, đang hoạt động qua IdentityService trước khi ghi lịch sử. Không đổi trạng thái văn bản.
- Thống kê công văn có JWT: tổng, tạo hôm nay UTC+7, chờ xử lý, Published, chờ OCR, dự thảo/chờ phê duyệt của tôi, chờ ký từng cấp, đã ký pháp nhân và số văn bản đã phân công. Aggregate toàn database, không lấy số lượng trang đầu.

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
POST /api/signatures/certificates/me/issue
GET  /api/signatures/certificates/me
GET  /api/signatures/certificates
GET  /api/signatures/certificates/stats
GET  /api/signatures/certificates/{userId}
DELETE /api/signatures/certificates/{userId}
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
- Trang chi tiết có modal sửa metadata, tìm/chọn người nhận phân công, xem PDF bằng blob URL và nút tải PDF. Payload tạo/sửa dùng `DocNumber`, `DocTypeId`, ngày `yyyy-MM-dd`; phân công dùng `ToUserId`; upload đọc đúng wrapper `ApiResponse<DocumentDto>`.
- Giao diện dùng nhận diện Trường Đại học Kiến trúc Hà Nội: logo HAU nền trong suốt, bảng màu xanh theo cổng sinh viên và phong cách glassmorphism.
- Hệ thống glassmorphism có ba cấp: sidebar kính tối, card nội dung kính sáng một màu và control kính bên trong; modal dùng lớp kính nổi riêng.
- Font giao diện thống nhất là Inter; icon chức năng dùng chung màu xanh HAU thay vì tô nhiều màu theo từng card.
- Giao diện có `-webkit-backdrop-filter`, fallback không blur và `prefers-reduced-motion`; desktop dùng sidebar đầy đủ, viewport rộng `450–960px` dùng thanh điều hướng đáy.
- Đã tách CSS theo Blazor CSS isolation: toàn bộ 17 page trong `Frontend/Pages` có file `.razor.css` cùng tên; `wwwroot/css/app.css` giữ theme, token và style dùng chung.
- Static inline style đã được loại bỏ khỏi các page. Chỉ giữ 3 style động phụ thuộc dữ liệu runtime: thanh/nhãn độ mạnh mật khẩu và màu hạn chứng thư.
- Dashboard đã được làm lại theo mẫu HAU Docs: tiêu đề/lời chào rõ hơn, tối đa 4 thẻ thống kê một hàng trên desktop, khối thao tác nhanh toàn chiều rộng và 1 cột khi viewport rộng không quá `960px`.
- Thao tác nhanh Dashboard có `Nhật ký hệ thống`, `Hoạt động ứng dụng` cho Admin và `Trung tâm thông báo` cho mọi tài khoản; cả ba trang đã đọc API thật, có bộ lọc, phân trang và cảnh báo lỗi nguồn.
- Dashboard đã đọc số liệu thật qua API Document/Identity/Sign. Chỉ Admin gọi thống kê người dùng/chứng thư; mỗi role dùng đúng trường nghiệp vụ. Nguồn lỗi/thiếu dữ liệu hiển thị `—`, cảnh báo và nút làm mới; giá trị 0 hợp lệ vẫn hiển thị 0.
- Đã bỏ sidebar thu gọn kiểu tablet; giao diện chỉ còn desktop trên `960px`, mobile từ `450px` đến `960px`, và khóa thao tác nếu rộng dưới `450px` hoặc cao dưới `500px`.
- Thẻ tài khoản mở popup có `Chỉnh sửa thông tin` và `Đăng xuất`; cập nhật họ tên/email/số điện thoại dùng API người dùng hiện tại.
- Nội dung mobile cuộn xuyên phía sau taskbar nổi; cuối trang tự chừa khoảng theo chiều cao taskbar để nội dung cuối không bị che.
- Trang ký số có nút `Quay lại`: từ chi tiết chữ ký trở về đúng chi tiết công văn, từ trang ký số chung trở về danh sách công văn.
- Nút primary dùng nền màu cố định `#304e8a` ở cả trạng thái thường và hover, không còn dùng gradient.
- Modal cấp chứng thư hỗ trợ tìm người dùng theo họ tên, username, email hoặc GUID; danh sách gợi ý hiển thị đủ thông tin để phân biệt người trùng tên và tự dùng GUID của mục được chọn.
- Ô chọn loại chứng thư dùng dropdown tùy biến đồng bộ với danh sách tìm người dùng, không còn phụ thuộc giao diện `<select>` mặc định của trình duyệt.
- Toàn bộ ô chọn vai trò, phòng ban cha, loại văn bản và bộ lọc công văn dùng component `CustomSelect`; frontend không còn `<select>` native và đã đọc đúng mảng `roles` từ API người dùng.
- Dropdown dài trong modal giới hạn chiều cao và cuộn nội bộ, giữ phần chân modal cùng các nút thao tác luôn hiển thị.
- Frontend có cache-busting cho stylesheet và Nginx không lưu cache HTML/CSS, tránh dropdown mất CSS isolation sau khi triển khai image mới.
- Dropdown vai trò được phép nổi vượt đáy modal, vẫn giới hạn 160px và cuộn nội bộ nên không bị cắt nội dung.
- Modal luôn nằm trên sidebar/taskbar; chỉ chuyển sang toàn màn hình trên mobile/máy tính nhỏ khi rộng không quá 1200px hoặc viewport rất thấp, cao không quá 650px. Desktop thông thường giữ popup căn giữa.
- Manager và Ban Giám hiệu có trang `Chứng thư của tôi` để tự tạo chứng thư bằng danh tính trong JWT; Manager nhận loại cá nhân, Ban Giám hiệu nhận loại pháp nhân. Clerk/Specialist không được tự cấp.
- Trang Admin Certificates đã dùng API danh sách/thu hồi thật và DTO cấp chứng thư đã đồng bộ với SignService.
- Modal Admin cấp chứng thư tách bộ lọc thành 3 cấp phụ thuộc: `Trường` → `Ban/Khoa/Phòng trực thuộc` → `Đơn vị cấp dưới`; tìm kiếm dùng cấp cụ thể nhất đã chọn. API lọc trực tiếp trong PostgreSQL theo `departmentId` và tự bao gồm các đơn vị con.

### Màn hình chính

- Login.
- First login/change password.
- Forgot password.
- Reset password.
- Dashboard.
- Admin System Logs và Admin Activity (API thật, lọc và phân trang).
- Notifications (dữ liệu riêng, đọc/chưa đọc, link chi tiết, polling 30 giây khi mở trang).
- Admin Users.
- Admin Departments.
- Admin Certificates.
- My Certificate.
- Documents list/create/detail.
- Documents OCR result.
- Signatures page.

### Tài liệu vận hành

- `tai_lieu/01_test_cases`: danh mục 52 test case, quy tắc và mẫu ghi test; `TC-AUTH-GMAIL-018` còn bước reset OTP thật chưa chạy.
- `tai_lieu/02_trien_khai_tung_service`: hướng dẫn triển khai riêng hạ tầng và từng service bằng Docker Compose.
- `tai_lieu/03_nhat_ky_thay_doi`: nhật ký thay đổi tóm tắt và mẫu cập nhật.
- `tai_lieu/04_tai_khoan_mac_dinh`: tài khoản ứng dụng seed, tài khoản hạ tầng local và hướng dẫn tạo user test theo role.
- Tài liệu lịch sử trong `tiendo/` vẫn được giữ nguyên và liên kết từ bộ tài liệu mới.

## 7. Trạng thái công việc sau rà soát

Rà soát ngày 05/10/2026 theo [nhật ký](3_nhat_ky.md), [kết quả test case](7_test_cases.md) và source. Các kết quả Pass dưới đây thuộc lần chạy đã ghi nhận trong tài liệu.

### 7.1. Đã hoàn thành và kiểm thử

- Ký nháy bằng Manager, ký pháp nhân bằng Ban Giám hiệu, phân quyền và xác minh PDF: `TC-SIGN-ROLE-012`; thao tác ký trên frontend: `TC-FE-SIGN-UI-013`.
- Workflow `Draft -> PendingDeptReview -> DeptSigned -> PendingDirectorSign -> DirectorSigned` và nút trình BGH: `TC-DOC-WF-016`, `TC-FE-WF-017`; có kiểm tra chặn BGH ký trực tiếp từ `DeptSigned`.
- OCR tự động qua upload/MinIO/Kafka/PaddleOCR và cập nhật kết quả: `TC-OCR-E2E-010`; PDF scan giả lập: `TC-OCR-SCAN-014`.
- Quên/reset mật khẩu qua Mailpit trên API và UI: `TC-AUTH-MAILPIT-015`; xác minh email lần đăng nhập đầu: `TC-AUTH-EMAIL-VERIFY-019`.
- Gmail SMTP thật đã gửi thành công và người nhận xác nhận email theo Công việc số 17 ngày 24/09/2026. Phần gửi/nhận email đã làm xong.
- Manager/Ban Giám hiệu tự cấp chứng thư, Admin quản lý chứng thư và lọc người dùng theo cơ cấu tổ chức: `TC-SIGN-CERT-SELF-SERVICE-038`, `TC-FE-CERT-DEPARTMENT-FILTER-039`, `TC-FE-CERT-ORG-CASCADE-040`.
- Lối vào ba trang log/hoạt động/thông báo đã có trên Dashboard: `TC-FE-DASHBOARD-QUICK-ACTIONS-043`; backend dữ liệu và UI thật hoàn thành trong Công việc số 46.
- Tích hợp công văn: sửa metadata, xem/tải PDF và phân công người nhận đã hoàn thành trên API/UI desktop/mobile; `TC-DOC-EDIT-045`, `TC-DOC-FILE-046`, `TC-DOC-ASSIGN-047`.
- Thống kê Dashboard thật theo 5 role, đếm đúng PostgreSQL/chứng thư, lỗi nguồn và làm mới desktop/mobile: `TC-DASHBOARD-STATS-048`, `TC-FE-DASHBOARD-STATS-049`.
- Log/audit/thông báo PostgreSQL, phân quyền, trace, callback OCR, đọc/chưa đọc, chứng thư sắp hết hạn, lưu sau recreate Gateway và UI desktop/mobile: `TC-GW-LOGS-050`, `TC-AUDIT-051`, `TC-NOTIFICATIONS-052`.

### 7.2. Phạm vi phần triển khai vừa hoàn thành

- Các phần 1–3 đã hoàn thành trong phạm vi ghi nhận. Nhật ký tập trung hiện bao gồm HTTP qua Gateway và startup; chưa phải bộ thu gom toàn bộ log nội bộ các service. Audit công văn qua trigger; các service còn lại qua Gateway. Thông báo cập nhật bằng polling khi mở trang, cảnh báo chứng thư được tạo khi đọc API thông báo; chưa có worker quét/push nền toàn hệ thống.

### 7.3. Kiểm thử thực tế — người dùng tự thực hiện

Theo yêu cầu ngày 05/10/2026, kiểm thử thực tế/nghiệm thu do người dùng tự thực hiện. Trạng thái hiện tại: **Chờ người dùng kiểm thử và xác nhận**. Các kết quả tự động, Docker local và mô phỏng đã ghi nhận vẫn được giữ, nhưng không thay cho nghiệm thu thực tế. Agent chỉ hỗ trợ phần này khi người dùng yêu cầu; không tự đánh dấu Pass hoặc tự chạy thử trên email/dữ liệu thực tế.

- [ ] Gmail: nhận OTP thật, reset mật khẩu và đăng nhập lại (`TC-AUTH-GMAIL-018`); phần gửi/nhận email lịch sử và reset Mailpit đã có kết quả riêng.
- [ ] OCR: dùng PDF/scan thực tế của nhà trường, đối chiếu số hiệu, ngày, tiêu đề và nội dung nhận diện.
- [ ] Công văn/ký số: tự thao tác tạo, upload, sửa, phân công, trình duyệt, ký PDF, xác minh chữ ký và phát hành theo các vai trò trên dữ liệu thực tế.
- [ ] Dashboard/log/audit/thông báo: đối chiếu số liệu, lịch sử và người nhận với thao tác thực tế; kiểm tra đọc/chưa đọc và liên kết chi tiết.

**Kiểm thử tự động còn có thể bổ sung:** coverage sâu workflow/ký/OCR. Hiện xUnit 86/86: Identity 35, Document 29, Gateway 21, Sign 1 test rỗng. Script API/UI: `tests/document-edit-file-assignment.spec.cjs`, `tests/dashboard-statistics.spec.cjs`, `tests/monitoring-notifications.spec.cjs`. Đây là công việc kỹ thuật riêng với nghiệm thu của người dùng.

### 7.4. Đề xuất cải tiến, chưa phải yêu cầu đã chốt

- Ký PDF và cập nhật workflow hiện là hai thao tác riêng, đã được kiểm thử theo luồng hiện tại. Có thể bổ sung ràng buộc hoặc gộp thao tác để trạng thái ký luôn đi cùng chữ ký PDF thành công nếu nghiệp vụ yêu cầu; không coi toàn bộ chức năng ký/workflow là chưa hoàn thành.

## 8. Cấu hình triển khai và secrets

- Docker Compose đã hỗ trợ file `.env` ở root project.
- Repo có `.env.example` để khai báo các biến cần đổi khi deploy: PostgreSQL, MinIO, JWT, OCR service-token, SMTP/email.
- `docker-compose.yml` dùng cú pháp `${VAR:-default_dev}` để local/dev vẫn chạy nếu chưa tạo `.env`.
- Docker Compose có Mailpit cho SMTP local: SMTP `1025`, Web UI/API `8025`.
- `.gitignore` và `.dockerignore` đã bỏ qua `.env`/`.env.*`, nhưng vẫn cho phép commit `.env.example`.
- `OCRService/.env.example` chỉ dùng khi chạy OCRService độc lập ngoài Docker Compose root.
