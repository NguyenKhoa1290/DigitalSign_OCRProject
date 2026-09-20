# Document Service

DocumentService quản lý metadata công văn, upload file lên MinIO, ghi log workflow và phát Kafka event `document.uploaded` cho OCRService.

## Chạy local

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
