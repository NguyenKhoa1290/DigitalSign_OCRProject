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
- Thông báo workflow theo người nhận/người tạo/role đang hoạt động. API lấy user từ JWT; đọc từng bản ghi hoặc tất cả chỉ ảnh hưởng user hiện tại. Cảnh báo chứng thư còn hiệu lực/hết hạn trong 30 ngày được tạo khi đọc danh sách; frontend nhận realtime trong ứng dụng qua SSE, không còn polling30 giây.
- Không lưu body/query/token/password; URL ngoài allowlist được che. Response `X-Trace-Id` liên kết HTTP với audit process theo W3C trace. API monitor/notification không tự tạo log vòng lặp, response no-store.
- Kiểm thử local: `tests/monitoring-notifications.spec.cjs`; xUnit bảo vệ route/action nằm trong `ApiGateway/tests/ApiGateway.Tests`.

- Local config trong `appsettings.json` trỏ đến `localhost:5048/5049/5050/5051`.
- Docker Compose override các destination sang hostname nội bộ: `identity-service`, `document-service`, `sign-service`, `ocr-service`.
- `JwtSettings` phải khớp với IdentityService và các downstream service.
- `AuthValidation:ValidateTokenUrl` trỏ tới IdentityService `/api/auth/validate-token`; Gateway gọi endpoint này sau khi JWT đã pass kiểm tra chữ ký/issuer/audience/expiry local để chặn token đã logout.
- Trong Docker, biến môi trường `AuthValidation__ValidateTokenUrl` đang trỏ tới `http://identity-service:8080/api/auth/validate-token`.
- `AuthValidation:FailOpenOnValidationError=true` cho phép Gateway fallback sang kết quả JWT local nếu IdentityService tạm lỗi/timeout. Token đã logout vẫn bị chặn khi IdentityService phản hồi `isValid=false`.


## Thông báo realtime trong ứng dụng (24/10/2026)

GET `/api/notifications/stream` yêu cầu Authorization Bearer, user lấy từ JWT, không nhận userId từ client. Luồng SSE gửi snapshot `{unreadCount}` lúc kết nối và khi thông báo thay đổi. PostgreSQL trigger INSERT/UPDATE/DELETE UserNotifications và thay đổi IsDeleted của Documents gọi pg_notify sau commit; Gateway LISTEN phát invalidation đúng user, không gửi nội dung/tài khoản khác. Cùng tài khoản ở nhiều tab/instance được cập nhật; database giữ dữ liệu và snapshot lúc nối lại bù sự kiện bỏ lỡ.

Heartbeat20 giây, kết nối tối đa2 phút để client lấy token hợp lệ và xác thực lại; frontend tự nối lại sau2 giây, giữ số gần nhất lúc gián đoạn. Nginx location stream tắt buffering/cache, timeout150 giây. Luồng giữ tối đa1 invalidation chờ/client, coalesce khi nhiều thay đổi; listener database tự nối lại và làm mới các kết nối. Mỗi request stream vẫn qua JWT validation/Identity kiểm tra như API khác.

Chuông toàn ứng dụng hiện số chưa đọc trên nền đỏ, ẩn số khi0, hiển thị99+ khi quá99. Trung tâm thông báo cập nhật danh sách từ sự kiện; đọc từng/tất cả hoặc Xem chi tiết cập nhật số giữa các tab. Đây là realtime khi ứng dụng đang mở, chưa có Web Push/FCM hoặc thông báo hệ điều hành lúc đóng ứng dụng. Kiểm tra hạn chứng thư vẫn chạy khi đọc danh sách như trước, chưa có scheduler theo thời gian.

## Kết quả OCR realtime cho người đang xem công văn (25/10/2026)

SSE hiện có nhận thêm query tùy chọn `documentId`: `/api/notifications/stream?documentId=<guid>`. User vẫn lấy từ JWT, không nhận userId tùy ý. Gateway kiểm tra user đang hoạt động và công văn còn hoạt động trước khi đăng ký, theo phạm vi GET công văn hiện tại (các tài khoản đăng nhập được xem hồ sơ hoạt động); không cấp thêm quyền chạy OCR. Thiếu/xóa hồ sơ hoặc user không hoạt động trả404.

Trigger `hau_document_result_trigger` phát `hau_document_updates` sau transaction commit khi OcrDataRaw thay đổi, khi IsDeleted thay đổi hoặc purge. Payload PostgreSQL chỉ DocId. NotificationListener nghe cả kênh thông báo riêng và kênh công văn; chỉ đánh thức kết nối đang theo dõi đúng DocId. Không phát tới mọi người dùng và không tạo bản ghi UserNotifications cho những người chỉ đang xem.

Snapshot vẫn chứa unreadCount của đúng user; khi đang theo dõi hồ sơ, thêm documentId/documentVersion. Không truyền nội dung OCR qua SSE. Client đọc lại GET công văn có xác thực để lấy kết quả; bản chụp lúc nối lại bù sự kiện bỏ lỡ. Nếu hồ sơ bị xóa/mất quyền đọc, documentVersion=null và client ngừng theo dõi, quay về stream thông báo thường. Kết nối/heartbeat/thời hạn2phút như trước. Nginx đã tắt buffering cho cùng route, query không cần cấu hình proxy mới.

Kiểm thử DocumentResultRealtimeTests: SSE HTTP thật + PostgreSQL schema cô lập, cùng công văn có Specialist/Manager/BoardOfDirectors xem đồng thời, giao dịch rollback không phát, commit đổi version cho đủ người xem, chỉ requester có unread, không gửi nội dung OCR/không đánh thức hồ sơ khác, thiếu/xóa/inactive bị chặn, reconnect đọc snapshot mới.


## Phạm vi thông báo công văn (05/11/2026)

Danh sách/chỉ số thông báo và SSE theo công văn kiểm tra phạm vi giống DocumentService, gồm đơn vị hiện tại, hồ sơ chung, người tạo, người được giao, người đã ký và Admin/BGH. Thông báo chờ ký nháy chỉ gửi Trưởng phòng thuộc đơn vị ghi trên công văn; hồ sơ thiếu đơn vị không phát cho toàn bộ Trưởng phòng.


## Giới hạn PDF xác minh (07/11/2026)

Route SignService cho phép request tối đa 64 MiB để hỗ trợ multipart PDF xác minh tối đa 50 MiB. Endpoint vẫn yêu cầu phiên đăng nhập hợp lệ. Đã kiểm tra upload 31 MiB qua frontend/gateway, không gặp 413.
