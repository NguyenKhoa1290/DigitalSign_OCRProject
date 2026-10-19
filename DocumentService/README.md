# Document Service

DocumentService quản lý metadata công văn, upload file lên MinIO, ghi log workflow và phát Kafka event `document.uploaded` cho OCRService.

File công văn hỗ trợ PDF, PNG, JPG/JPEG và WEBP, tối đa 50 MiB. API trả `fileName` gốc và `fileContentType`; endpoint `/api/documents/{id}/file` trả MIME tương ứng, thêm `?download=true` để tải xuống đúng tên gốc. Thay file thành công sẽ xóa object cũ; cơ chế này áp dụng cả khi đổi PDF sang ảnh hoặc ngược lại. OCRService giới hạn 5 trang mỗi lượt; ký số hiện vẫn dùng PDF.

## Chạy local

Luồng xóa mềm đã triển khai theo [chính sách](../tai_lieu/06_thiet_ke_he_thong/PHAN_QUYEN_XOA_CONG_VAN.md): DELETE chỉ dành cho Admin/Clerk/Specialist đủ điều kiện, nhận JSON `reason`; Admin bắt buộc nhập lý do. Không xóa Rejected hoặc hồ sơ đã qua nghiệp vụ. `GET /api/documents/deleted` và `POST /api/documents/{id}/restore` chỉ dành cho Admin. `POST /api/documents/{id}/receive` ghi tiếp nhận chính thức công văn đến cho Clerk/Admin.

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
```

## Workflow hiện tại

```text
Draft
  -> PendingDeptReview
  -> DeptSigned
  -> PendingDirectorSign
  -> DirectorSigned
  -> Published
```

`DeptSignAsync` chuyển `PendingDeptReview` sang `DeptSigned`. Sau đó gọi `SubmitToDirectorAsync` hoặc `POST /api/documents/{id}/submit-director` để chuyển sang `PendingDirectorSign`.

## Lưu ý tích hợp

- File upload lên MinIO bucket `documents`.
- `Document.MinioPath` được lưu dạng `documents/{storedFileName}`.
- Kafka event gửi `minio_path = storedFileName`, tức object name không kèm bucket prefix.
- Kafka event hiện gửi `authToken` rỗng; OCRService sẽ fallback sang `SERVICE_TOKEN` để PATCH kết quả OCR về DocumentService.
- Endpoint `PATCH /api/documents/{id}/ocr` nhận JWT người dùng hoặc header nội bộ `X-Service-Token`.
- `SERVICE_TOKEN` của OCRService phải khớp với `ServiceAuth:OcrServiceToken` của DocumentService.
