# OCR Service

Microservice OCR cho hệ thống HAU DigitalSign OCR.

## Định dạng và giới hạn đầu vào

- Nhận PDF hoặc ảnh tĩnh PNG, JPG/JPEG, WEBP; mỗi ảnh là một trang. Ảnh điện thoại được xoay theo EXIF trước khi OCR.
- Mỗi lượt tối đa **5 trang** cho các route process, process-upload, export-word-upload và gemini/export-word-upload, cũng như xử lý qua Kafka. PDF vượt giới hạn trả HTTP 400 trước khi render/chạy PaddleOCR/gọi Gemini; không tự cắt bớt trang. Cần tách PDF trước khi quét.
- Xuất Word qua Gemini tối đa 25 MiB/tệp. Lưu file công văn tại DocumentService vẫn tối đa 50 MiB; PDF nhiều hơn 5 trang có thể lưu/xem nhưng không chạy OCR cả tệp.
- Kiểm thử đầu vào: `PYTHONPATH=OCRService python OCRService/tests/test_document_inputs.py -v` trong môi trường có dependencies và Poppler. OCR/cloud dùng mock, không gọi Gemini trả phí.

## Stack

- Python 3.10+
- FastAPI
- PaddleOCR tiếng Việt
- pdf2image
- Poppler
- MinIO
- Kafka tùy chọn

## Cấu trúc

```text
OCRService/
├── app/
│   ├── main.py                    FastAPI entry point, lifespan, CORS, router
│   ├── config.py                  Settings từ environment/.env
│   ├── api/routes.py              REST endpoints
│   ├── ocr/engine.py              Wrapper PaddleOCR
│   ├── ocr/extractor.py           Bóc tách trường công văn
│   └── services/
│       ├── ocr_processor.py       Pipeline MinIO -> OCR -> DocumentService
│       ├── minio_service.py       Download file từ MinIO
│       ├── document_service.py    PATCH kết quả OCR về DocumentService
│       └── kafka_consumer.py      Kafka consumer background thread
├── requirements.txt
└── README.md
```

## Cài đặt

### 1. Cài Poppler

`pdf2image` cần Poppler.

Windows:

```bash
# tải Poppler Windows, giải nén, thêm thư mục bin vào PATH
# https://github.com/oschwartz10612/poppler-windows/releases
```

Conda:

```bash
conda install -c conda-forge poppler
```

### 2. Tạo virtual environment

```bash
cd OCRService
python -m venv venv
venv\Scripts\activate
```

### 3. Cài dependencies

```bash
pip install -r requirements.txt
```

Lần đầu chạy PaddleOCR có thể tải model, cần kết nối internet. Nếu triển khai offline, nên chạy thử OCR một lần trên máy có internet hoặc chuẩn bị sẵn cache/model PaddleOCR trước khi đóng gói image/môi trường.

### 4. Cấu hình

Tạo `.env` nếu cần override cấu hình mặc định trong `app/config.py`.

Các biến đáng chú ý:

```text
MINIO_ENDPOINT=localhost:9000
MINIO_ACCESS_KEY=minioadmin
MINIO_SECRET_KEY=minioadmin
MINIO_BUCKET=documents
DOCUMENT_SERVICE_URL=http://localhost:5049
SERVICE_TOKEN=hau-dev-ocr-service-token
KAFKA_ENABLED=false
KAFKA_BOOTSTRAP_SERVERS=localhost:9092
KAFKA_TOPIC_DOCUMENT_UPLOADED=document.uploaded
OCR_LANGUAGE=vi
OCR_USE_GPU=false
```

## Chạy service

```bash
uvicorn app.main:app --host 0.0.0.0 --port 5051 --reload
```

Swagger:

```text
http://localhost:5051/docs
```

## Endpoints

| Method | Endpoint | Mô tả |
|---|---|---|
| POST | `/api/ocr/process` | OCR từ MinIO object name |
| POST | `/api/ocr/process-upload` | Upload PDF và OCR trực tiếp để test |
| GET | `/api/ocr/health` | Health check |

## Test nhanh

Upload PDF trực tiếp:

```bash
curl -X POST "http://localhost:5051/api/ocr/process-upload?doc_id=test" \
  -F "file=@congvan.pdf"
```

OCR từ MinIO:

```bash
curl -X POST "http://localhost:5051/api/ocr/process" \
  -H "Content-Type: application/json" \
  -d '{
    "doc_id": "3fa85f64-0000-0000-0000-000000000000",
    "minio_path": "stored-file-name.pdf",
    "token": "eyJ..."
  }'
```

Lưu ý:

- Nếu truyền `token` trong request, giá trị này nên là raw JWT, không kèm chữ `Bearer`; code sẽ tự gửi header `Authorization: Bearer {token}`.
- Nếu `token` rỗng và `SERVICE_TOKEN` có cấu hình, OCRService sẽ gọi DocumentService bằng header nội bộ `X-Service-Token`.

## Pipeline OCR

```text
process_document(doc_id, minio_path, token)
  -> download PDF từ MinIO bằng minio_path
  -> convert PDF sang ảnh bằng pdf2image
  -> chạy PaddleOCR từng trang
  -> bóc tách doc_number, issued_date, title, issuing_org
  -> PATCH /api/documents/{doc_id}/ocr bằng JWT hoặc SERVICE_TOKEN
  -> trả kết quả OCR
```

Payload gửi về DocumentService:

```json
{
  "docNumber": "...",
  "title": "...",
  "issuedDate": "2026-09-14",
  "ocrDataRaw": "{...json string...}"
}
```

## Kafka

Consumer chỉ chạy khi:

```text
KAFKA_ENABLED=true
```

Payload kỳ vọng:

```json
{
  "doc_id": "uuid",
  "minio_path": "stored-file-name.pdf",
  "token": "raw-jwt"
}
```

Điểm cần lưu ý theo code hiện tại:

- DocumentService publish Kafka event sau upload file.
- DocumentService hiện vẫn gửi `token` rỗng trong Kafka event.
- Khi Kafka event không có JWT, OCRService dùng biến môi trường `SERVICE_TOKEN` để PATCH kết quả về DocumentService qua header `X-Service-Token`.
- `SERVICE_TOKEN` phải khớp với `ServiceAuth:OcrServiceToken` của DocumentService.

## Trường bóc tách

| Trường | Mô tả |
|---|---|
| `doc_number` | Số hiệu văn bản, ví dụ `123/QD-HAU` |
| `issued_date` | Ngày ban hành, dạng ISO khi trả response |
| `title` | Trích yếu/nội dung chính |
| `issuing_org` | Cơ quan ban hành |
| `ocr_data_raw` | JSON raw gồm pages, lines, confidence và extracted fields |

## Dựng Word nháp bằng PaddleOCR và Gemini

Văn thư/Admin mở `/documents/ocr-scan`, chọn model Gemini khả dụng, tải PDF hoặc ảnh và tải về DOCX nháp để rà soát. Nhận một PDF nhiều trang hoặc tối đa100 ảnh, tổng tối đa25 MB; mỗi lượt chọn tối đa5 trang/ảnh. Nếu Google báo hết quota hoặc model quá tải, chọn model khác trong danh sách rồi gửi lại.

Admin cấu hình hoặc xóa API key tại `/admin/gemini-settings`. Key được kiểm tra với Google, mã hóa bằng ASP.NET Data Protection và không gửi xuống trình duyệt. OCRService lấy key qua endpoint nội bộ IdentityService bằng `SERVICE_TOKEN`; IdentityService cần giữ Data Protection key ring bền vững ở `DataProtection:KeysPath` (Docker Compose mount volume `hau_identity_keys`).

Các route qua API Gateway:

| Method | Route | Vai trò |
|---|---|---|
| `GET` | `/api/ocr/gemini/models` | Admin, Văn thư |
| `POST` | `/api/ocr/gemini/export-word-upload?model_id={id}` | Admin, Văn thư |

Endpoint nhận multipart `file` (tương thích cũ) hoặc danh sách `files`, query `pages` như `1-5`/`1,3,8-10` và `model_id`. Không trộn PDF với ảnh. Trang được loại trùng, sắp theo thứ tự gốc, kiểm tra trong phạm vi và tối đa5 trước khi nhận diện/gọi AI. Thiếu pages giữ hành vi cũ: quét tất cả nếu không quá5 trang. `POST /api/ocr/input-info` nhận file và trả totalPages, chỉ đọc số trang, không gọi AI. Response Word có header X-OCR-Pages ghi số trang nguồn đã chọn.

Trang quét xem trước PDF bằng blob URL tại máy người dùng, có liên kết mở riêng nếu trình duyệt điện thoại không hỗ trợ iframe; ảnh hiển thị thumbnail theo thứ tự chọn. Bấm Chạy OCR mở hộp chọn trang (mặc định1), sau đó Tạo và tải Word mới chạy nhận diện. Nếu429/lỗi giữ lựa chọn để đổi model/thử lại. Nút gửi/chọn tệp khóa khi xử lý; blob URL được giải phóng khi thay tài liệu/rời trang. Chức năng nhiều ảnh/chọn trang mới thuộc trang Quét OCR và xuất Word; tệp gắn với một công văn và OCR thông tin ở trang chi tiết giữ nguyên.

 PaddleOCR hiện cung cấp các dòng OCR và tọa độ; Gemini đọc ảnh cùng bằng chứng này để trả layout JSON, sau đó OCRService tạo DOCX có thể sửa. PP-Structure chưa được tích hợp vào endpoint này. Đây là bản nháp, cần Văn thư kiểm tra; việc build/smoke test không xác nhận chất lượng nội dung hoặc bố cục trên mọi tài liệu.
