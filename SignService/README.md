# Sign Service

SignService cấp chứng thư số nội bộ, ký PDF và xác minh chữ ký. Service dùng iText7 + BouncyCastle, PostgreSQL và MinIO.

## Chạy local

Từ triển khai xóa mềm 19/10/2026, projection Documents kiểm tra IsDeleted. Cần áp dụng migration DocumentService trước khi cập nhật SignService. Luồng ký commit SigningStartedAt trước khi chạm PDF, giữ khóa hàng PostgreSQL xuyên thao tác ký/lưu chữ ký. Mốc này giữ lại khi ký lỗi vì tệp có thể đã thay đổi; hồ sơ đó không được xóa như bản nháp mới. Migration DocumentDeletionProjection chỉ đồng bộ EF snapshot, không sửa bảng Documents.

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
POST   /api/signatures/incoming/{id}/direction
POST   /api/signatures/incoming/{id}/assignment
GET    /api/signatures/document/{docId}
GET    /api/signatures/document/{docId}/verify
POST   /api/signatures/certificates/issue
POST   /api/signatures/certificates/me/issue
GET    /api/signatures/certificates/me
GET    /api/signatures/certificates
GET    /api/signatures/certificates/{userId}
DELETE /api/signatures/certificates/{userId}
```

## Ký công văn đến

Chiều đến dùng thứ tự Ban Giám hiệu ký `IncomingDirection`, rồi Manager ký `IncomingAssignment`. Người ký lấy từ JWT và được kiểm tra lại vai trò/trạng thái tại IdentityService. Cấu hình `IdentityService:BaseUrl` cho danh bạ; Compose dùng `http://identity-service:8080/`.

Chỉ đạo yêu cầu `ExpectedVersion`, `DepartmentId` và `Comment`; phân công yêu cầu `ExpectedVersion`, `ToUserId` và `Comment`. Manager phải thuộc đúng đơn vị được chỉ đạo; người nhận là Specialist đang hoạt động trong đơn vị hoặc đơn vị trực thuộc. Ý kiến và đích nhận được ghi vào Reason của chữ ký, cùng lịch sử xử lý; chưa vẽ bút phê lên nội dung trang PDF.

Ký và xác minh PDF trước khi chuyển trạng thái. Mỗi bước tạo object MinIO mới, giữ bản gốc và bản ký chỉ đạo. Khóa hàng PostgreSQL và token phiên bản chặn gửi lặp; trạng thái, bằng chứng ký và lịch sử cùng giao dịch. Phải flush đơn vị mới trước khi insert lịch sử để trigger thông báo đọc đúng đơn vị. Lỗi giao dịch không thay đường dẫn bản đang dùng; có thể còn object mới chưa được tham chiếu để xử lý dọn sau.

Các endpoint `personal-sign`/`legal-seal` dành cho chiều đi từ chối `CongVanDen`. Chữ ký chiều đến xem và xác minh bằng các endpoint document/verify hiện có.

## Chứng thư số

- Khi startup, service tự khởi tạo Root CA nếu chưa có.
- Docker mount thư mục cert vào volume `hau_sign_certs` tại `/app/certs`.
- Cần backup volume `hau_sign_certs` nếu muốn mang dữ liệu chữ ký/certificate sang máy khác.
- `Admin` được cấp, xem danh sách và thu hồi chứng thư của người dùng.
- `Manager` và `BoardOfDirectors` được tự tạo chứng thư cho chính mình qua endpoint `/certificates/me/issue`; danh tính lấy từ JWT, client không được truyền `UserId`.
- `Manager` nhận chứng thư `Personal`; `BoardOfDirectors` nhận chứng thư `Organization`.
- Chứng thư tự cấp còn hiệu lực không được cấp trùng; API trả HTTP `409`.

## Lưu ý tích hợp hiện tại

- Backend `SignRequestDto` yêu cầu `DocId` và `SignerId`.
- Frontend đã đồng bộ DTO ký số để gửi đúng `DocId`, `SignerId`, `SignerName`, `Reason`.
- SignService đọc `Documents.MinioPath` từ database dùng chung, chuẩn hóa `documents/{storedFileName}` thành object `{storedFileName}` rồi tải/lưu lại đúng file trên MinIO.
- Luồng backend ký nháy end-to-end qua Docker/Gateway đã pass với test case `TC-SIGN-001`.
- Payload theo frontend mới đã pass test case `TC-FE-SIGN-002` cho cả ký nháy và ký pháp nhân.
