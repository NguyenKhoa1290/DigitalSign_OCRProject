# OCR Service

Microservice nhận diện ký tự quang học (OCR) cho hệ thống quản lý công văn HAU.

## Stack
- **Python 3.10+**
- **FastAPI** — REST API
- **PaddleOCR** — OCR engine (tiếng Việt)
- **pdf2image** — Chuyển PDF → ảnh (cần cài Poppler)
- **Kafka** — Nhận sự kiện `document.uploaded` (optional)
- **MinIO** — Tải file PDF

## Cài đặt

### 1. Cài Poppler (cần thiết cho pdf2image)
```bash
# Windows: tải từ https://github.com/oschwartz10612/poppler-windows/releases
# Giải nén và thêm bin/ vào PATH

# Hoặc dùng conda:
conda install -c conda-forge poppler
```

### 2. Tạo virtual environment
```bash
cd OCRService
python -m venv venv
venv\Scripts\activate        # Windows
# source venv/bin/activate   # Linux/Mac
```

### 3. Cài dependencies
```bash
pip install -r requirements.txt
```

> ⚠️ PaddleOCR lần đầu chạy sẽ tự tải model (~500MB). Cần kết nối internet.

### 4. Cấu hình
```bash
copy .env.example .env
# Chỉnh sửa .env theo môi trường của bạn
```

### 5. Chạy service
```bash
uvicorn app.main:app --host 0.0.0.0 --port 5051 --reload
```

## Endpoints

| Method | Endpoint | Mô tả |
|---|---|---|
| `POST` | `/api/ocr/process` | OCR từ MinIO path |
| `POST` | `/api/ocr/process-upload` | Upload PDF test trực tiếp |
| `GET` | `/api/ocr/health` | Health check |
| `GET` | `/docs` | Swagger UI |

## Test nhanh

### Upload PDF test
```bash
curl -X POST "http://localhost:5051/api/ocr/process-upload?doc_id=test" \
  -F "file=@congvan.pdf"
```

### Kích hoạt OCR từ MinIO
```bash
curl -X POST "http://localhost:5051/api/ocr/process" \
  -H "Content-Type: application/json" \
  -d '{
    "doc_id": "3fa85f64-...",
    "minio_path": "3fa85f64-....pdf",
    "token": "Bearer eyJ..."
  }'
```

## Workflow tích hợp

```
Văn thư upload PDF
    → DocumentService lưu MinIO, trả về doc_id + minio_path
    → Văn thư gọi POST /api/ocr/process { doc_id, minio_path, token }
    → OCR Service chạy PaddleOCR
    → Tự động cập nhật DocumentService (PATCH /api/documents/{id}/ocr)
    → Văn thư xem kết quả đã điền sẵn, kiểm tra và lưu
```

## Trường bóc tách

| Trường | Ví dụ | Ghi chú |
|---|---|---|
| `doc_number` | `123/QĐ-HAU` | Số hiệu công văn |
| `title` | `V/v thông báo lịch thi...` | Trích yếu nội dung |
| `issued_date` | `2024-09-01` | Ngày ban hành |
| `issuing_org` | `Trường ĐH Kiến Trúc HN` | Cơ quan ban hành |
| `ocr_data_raw` | JSON | Toàn bộ kết quả thô (tọa độ, confidence) |
