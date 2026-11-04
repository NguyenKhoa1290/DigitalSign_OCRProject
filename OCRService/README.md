# OCR Service

## Luồng hiện hành từ ngày 26/10/2026

Tải tệp chỉ lưu nguồn; không còn tự chạy Paddle qua `document.uploaded`. Endpoint `/api/ocr/process` và callback xử lý thông điệp cũ trả 410 trước tải nguồn/gọi AI. Các mô tả Kafka tự động theo upload bên dưới là nhánh cũ, không dùng để triển khai mới.

Trong chi tiết công văn, người dùng chọn model Gemini và tối đa 5 trang; DocumentService lưu OcrJobs và dùng Kafka `ocr.gemini.requested.v1`. OCRService nội bộ vẫn kết hợp bằng chứng Paddle với Gemini. Văn thư chỉ chạy/hiệu chỉnh chiều đến, Admin hỗ trợ; Chuyên viên không chạy OCR. Kiểm tra quyền/chiều ở DocumentService áp dụng cho attached input-info, nhận diện, job và kết quả. Giữ nguyên trang quét độc lập và quyền xem kết quả lịch sử.

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


## Kết quả OCR gắn với công văn

- `POST /api/ocr/gemini/recognize-upload`: cùng đầu vào `file`/`files`, `model_id`, `pages` như xuất Word; trả `{provider,schemaVersion,modelId,selectedPages,sourceFileNames,createdAt,layout}`. Chạy chung pipeline Paddle evidence + Gemini, tối đa5 trang đã chọn. Không bóc trường để tự ghi đè metadata hồ sơ.
- `POST /api/ocr/gemini/export-layout`: body `{layout}`; tạo DOCX từ kết quả đã lưu mà không gọi lại Paddle/Google. Giới hạn5 trang,500 khối,10000 ô/2000 đoạn, JSON2MB; kiểm tra cấu trúc trước dựng Word. Hai route đều dùng policy Admin/Clerk của Gateway.
- Luồng chi tiết hiện lưu JSON layout ở backend qua hàng đợi DocumentService (xem phần OCR nền bên dưới), không lưu bản DOCX phụ trong MinIO. Trang quét độc lập vẫn dùng export-word-upload như cũ. Paddle vẫn hỗ trợ Gemini ở backend; các trường/JSON Paddle cũ được bỏ khỏi giao diện công văn.


## Nhận diện tệp đã đính kèm công văn (25/10/2026)

`POST /api/ocr/gemini/documents/{id}/input-info` nhận `{expectedVersion}`, trả totalPages mà không gọi AI. `/recognize` nhận `{expectedVersion,modelId,pages}` để chạy chính tệp đã lưu, trả cùng cấu trúc layoutJSON như recognize-upload. Forward BearerJWT tới `/api/documents/{id}/ocr-state`; kiểm tra tồn tại/phiên bản, lấy MinIO object từ server, bỏ qua đường dẫn do client tự gửi. Thiếu tệp trả400, mất object404, phiên bản cũ409, quá25MB413; các trường hợp này không gọi Gemini. Giữ chính sách Admin/Clerk của Gateway.

MinIO đọc tối đa25MB+1, luôn close/release_conn kể cả quá giới hạn. Bước đếm và bước chạy đều kiểm tra lại trạng thái tệp; chỉ render tối đa5 trang đã chọn. Hai route công khai này dùng JWT người dùng; tác vụ nền dùng route nội bộ riêng bên dưới. Không upload lại file vào MinIO hoặc đi vòng qua điện thoại. Trang quét độc lập giữ nguyên upload/multi-image. Bước gửi hàng đợi kiểm tra ExpectedVersion; tải Word từ kết quả đã lưu không gọi lại AI.

## OCR nền của công văn (25/10/2026)

Trang chi tiết gửi lệnh qua DocumentService, không chờ `/recognize` trả layout. Worker gọi `POST /api/ocr/internal/gemini-job` với `{docId,sourcePath,modelId,pages}` và `X-Service-Token` khớp `SERVICE_TOKEN`. Token sai/trống bị chặn trước truy cập tệp hoặc gọi AI. Endpoint không công bố trên OpenAPI. Kiểm tra công văn còn tồn tại và đường dẫn nguồn còn khớp qua DocumentService; đọc MinIO có giới hạn25MB và chạy cùng pipeline bằng chứng Paddle + Gemini, chỉ các trang đã chọn (tối đa5). Trả layout JSON cho worker lưu cùng thông báo thành công/lỗi. Không nhận JWT người dùng hoặc API key trong bản ghi tác vụ.

Service token dùng chung phải cấu hình ở cả hai dịch vụ; DocumentService cần `OcrService__BaseUrl=http://ocr-service:5051/` trong Docker. Các route đồng bộ giữ tương thích và trang quét độc lập vẫn dùng export-word-upload.

Kafka cho lệnh OCR Gemini: DocumentService dùng OcrJobOutbox/OcrJobWorker publish/consume topic ocr.gemini.requested.v1 rồi gọi endpoint nội bộ trên. Consumer Python document.uploaded hiện có vẫn là luồng Paddle cũ, không nhận topic Gemini và không tự chọn model thay người dùng. Giới hạn tệp/trang và pipeline bằng chứng Paddle + Gemini của endpoint nội bộ giữ nguyên.

## Bổ sung OCR cả hai chiều ngày 04/11/2026

Phạm vi hiện hành thay hạn chế chiều đi ở ghi chú ngày 25–26/10: Văn thư chạy OCR và tải Word hiệu chỉnh ở cả hai chiều khi Draft/Rejected. Quyền sửa thông tin/thay tệp nguồn/trình chiều đi vẫn thuộc Chuyên viên là người tạo hoặc Admin hỗ trợ. Chuyên viên không chạy OCR; riêng người tạo chiều đi được tải Word nhận diện/bản sửa hiện hành để hoàn thiện nội dung bên ngoài.

Tệp chính .docx là văn bản hoàn chỉnh, không hiện OCR và bị chặn trước gọi AI. Ảnh/PDF dùng model và tối đa 5 trang đã chọn; upload không tự OCR. Văn thư không gửi OCR/Word hiệu chỉnh sau khi chuyển duyệt/ký/phát hành; kiểm tra lại trước AI và lúc lưu, không ghi đè tệp/chữ ký nguồn. Admin giữ ngoại lệ hỗ trợ đã thống nhất. Chưa tự chuyển Word sang PDF hoặc phân loại PDF có lớp chữ.

GET /api/documents/{id}/ocr-word?expectedVersion=... cho Admin/Clerk hoặc Specialist là người tạo chiều đi, lấy layout từ server và chặn phiên bản cũ. Có bản Word hiệu chỉnh thì trả nguyên bytes/tên; nếu chưa có, dựng DOCX qua POST /api/ocr/internal/export-layout với X-Service-Token, không gọi AI. Route nội bộ từ chối token sai/trống; không mở public export-layout cho Specialist. Trạng thái/nhãn và kết quả realtime giữ nguyên. Thiết kế hiện hành: tai_lieu/06_thiet_ke_he_thong/PHAN_QUYEN_TAO_VA_TRINH_CONG_VAN.md.
