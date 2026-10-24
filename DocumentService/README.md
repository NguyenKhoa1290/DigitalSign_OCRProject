# Document Service

DocumentService quản lý metadata công văn, upload file lên MinIO, ghi log workflow và phát Kafka event `document.uploaded` cho OCRService.

File công văn hỗ trợ PDF, Word `.docx`, PNG, JPG/JPEG và WEBP, tối đa 50 MiB. API trả `fileName` gốc và `fileContentType`; endpoint `/api/documents/{id}/file` trả MIME tương ứng, thêm `?download=true` để tải xuống đúng tên gốc. Thay file thành công sẽ xóa object cũ; chưa lưu lịch sử phiên bản tệp. Word được coi là tệp văn bản hoàn chỉnh, không cần OCR, không phát Kafka event OCR và không tính vào thống kê chờ OCR. OCRService chỉ xử lý PDF/ảnh, tối đa 5 trang mỗi lượt. Ký số vẫn cần PDF.

Văn thư có thể tải Word đã sửa bằng Word trên máy lên công văn Nháp, tải lại đúng tên/MIME gốc. Giao diện hiện thông tin bản Word và nút tải xuống, không nhúng Word vào trình xem PDF hoặc gửi tệp lên dịch vụ bên ngoài. Sau khi sửa, xuất PDF rồi tải lên thay Word trước khi trình ký. Chưa hỗ trợ `.doc` cũ, chuyển đổi Word sang PDF tự động hoặc giữ đồng thời Word/PDF như hai bản đính kèm.

## Chạy local

Luồng xóa mềm đã triển khai theo [chính sách](../tai_lieu/06_thiet_ke_he_thong/PHAN_QUYEN_XOA_CONG_VAN.md): DELETE chỉ dành cho Admin/Clerk/Specialist đủ điều kiện, nhận JSON `reason`; Admin bắt buộc nhập lý do. Không xóa Rejected hoặc hồ sơ đã qua nghiệp vụ. `GET /api/documents/deleted` và `POST /api/documents/{id}/restore` chỉ dành cho Admin. `POST /api/documents/{id}/receive` ghi tiếp nhận chính thức công văn đến cho Clerk/Admin.

Từ 24/10/2026, `POST /api/documents/{id}/cancel` là nhánh hủy riêng **chỉ Admin**, nhận `reason` bắt buộc (tối đa 1000 ký tự) và `expectedVersion`. Áp dụng mọi trạng thái, kể cả hồ sơ cũ, đã ký hoặc Published; không áp dụng điều kiện xóa bản nháp cho nhánh này. Hủy đánh dấu IsDeleted, giữ trạng thái gốc/tệp/chữ ký/lịch sử và ghi Cancel cùng giao dịch dưới khóa hàng. API thông thường và Sign/OCR không truy cập được hồ sơ đã hủy. Danh sách đã xóa trả `isCancelled`; hồ sơ hủy không được khôi phục qua chức năng khôi phục bản nháp. Không có xóa vật lý hoặc thao tác hủy hàng loạt.

`expectedVersion` phải có mặt và không null. GUID toàn số 0 là giá trị hợp lệ cho hồ sơ cũ do migration xóa mềm đã đặt mặc định này; chỉ chấp nhận khi khớp giá trị hiện tại trong DB. Không đổi hàng loạt phiên bản hoặc bỏ kiểm tra xung đột. Sau hủy, phiên bản được đổi thành GUID mới.

Migration chạy khi khởi động; sao lưu database trước cập nhật. Hồ sơ trước migration giữ `DeletionEligibilityConfirmed=false`, cần đối soát riêng, không tự cấp quyền xóa từ Draft/Submit. Luồng mới ghi Create và lưu người tạo bất biến. Xóa/khôi phục có audit trong một giao dịch, giữ nguyên tệp; OCR dùng `GET /api/documents/{id}/ocr-state` để lấy phiên bản trước xử lý và ExpectedVersion khi callback. API đọc thường không trả hồ sơ đã xóa.

Khởi động hạ tầng trước:

```bash
docker compose up -d postgres minio kafka minio-init
```

Sau đó chạy service:

```bash
dotnet run --project DocumentService/src/DocumentService.API/DocumentService.API.csproj
```

Port mặc định:

```text
http://localhost:5049
```

## Chạy bằng Docker

```bash
docker compose up -d document-service
```

Hoặc chạy toàn bộ hệ thống:

```bash
docker compose up -d
```

## API chính

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
POST   /api/documents/{id}/incoming-submit
```

## Workflow công văn đi

```text
Draft
  -> PendingDeptReview
  -> DeptSigned
  -> PendingDirectorSign
  -> DirectorSigned
  -> Published
```

`DeptSignAsync` chuyển `PendingDeptReview` sang `DeptSigned`. Sau đó gọi `SubmitToDirectorAsync` hoặc `POST /api/documents/{id}/submit-director` để chuyển sang `PendingDirectorSign`.

## Workflow công văn đến

`CongVanDen` dùng chuỗi `Draft -> PendingDirection -> PendingAssignment -> IncomingAssigned`. Clerk trình Ban Giám hiệu bằng `incoming-submit` với `ExpectedVersion`; thao tác này tiếp nhận chính thức nếu hồ sơ chưa có mốc tiếp nhận. Cần PDF để ký; ảnh vẫn dùng được cho OCR.

Ban Giám hiệu ký chỉ đạo và chọn đơn vị qua `POST /api/signatures/incoming/{id}/direction`. Manager của đúng đơn vị ký phân công chuyên viên qua `POST /api/signatures/incoming/{id}/assignment`. Hai bước ký cập nhật PDF, trạng thái, bằng chứng chữ ký và lịch sử trong luồng riêng tại SignService. Clerk không có quyền gọi `assign`.

Các API trình/ký nháy/ký pháp nhân/phát hành và phân công không ký của chiều đi từ chối `CongVanDen`. Không tự chuyển trạng thái hồ sơ cũ; hồ sơ đang Nháp nhưng đã có bằng chứng ký cần đối soát. Chưa có thao tác thời hạn, báo cáo kết quả hoặc xác nhận hoàn thành công việc trong nhánh mới.

## Lưu ý tích hợp

- File upload lên MinIO bucket `documents`.
- `Document.MinioPath` được lưu dạng `documents/{storedFileName}`.
- Kafka event gửi `minio_path = storedFileName`, tức object name không kèm bucket prefix.
- Kafka event hiện gửi `authToken` rỗng; OCRService sẽ fallback sang `SERVICE_TOKEN` để PATCH kết quả OCR về DocumentService.
- Endpoint `PATCH /api/documents/{id}/ocr` nhận JWT người dùng hoặc header nội bộ `X-Service-Token`.
- `SERVICE_TOKEN` của OCRService phải khớp với `ServiceAuth:OcrServiceToken` của DocumentService.


## Xóa vĩnh viễn công văn đã xóa/hủy (24/10/2026)

Admin dùng `POST /api/documents/{id}/purge` với `reason`, `confirmationTitle` đúng tên và `expectedVersion` hiện hành (GUID0 được chấp nhận nếu khớp dữ liệu cũ). Chỉ áp dụng hồ sơ `IsDeleted`; hồ sơ đang hoạt động phải hủy trước. Giao diện Công văn đã xóa đặt nút Xóa vĩnh viễn cạnh Khôi phục; hồ sơ Cancel chỉ có Xóa vĩnh viễn.

Khóa hàng PostgreSQL, xóa Document/DocumentProcesses/Signatures và thông báo có FK trong một transaction; giữ bản ghi độc lập `DocumentPurgeRecords` chứa người thực hiện, lý do, tên, phiên bản và thời điểm. Dọn các đường dẫn tệp hiện hành/gốc/chỉ đạo sau commit, không xóa tệp còn được hồ sơ khác tham chiếu. Lỗi lưu trữ được lưu bền để worker thử lại mỗi30 giây, trả `filesPendingCleanup=true`. Không tự purge hồ sơ cũ; worker chỉ dọn yêu cầu purge đã được Admin xác nhận. Migration `20261024103037_AddDocumentPurgeRecords` chỉ tạo bảng nhật ký/hàng đợi.
