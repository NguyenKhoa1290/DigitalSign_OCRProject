# Document Service

DocumentService quản lý metadata công văn, upload tệp lên MinIO, ghi workflow và phân phối tác vụ OCR do người dùng yêu cầu qua Kafka `ocr.gemini.requested.v1`. Tải tệp không tự chạy OCR hoặc phát `document.uploaded`.

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
- Nhánh `document.uploaded` tự OCR sau upload đã dừng từ 26/10/2026; không gửi đường dẫn/JWT trong lệnh cũ. Tác vụ Gemini dùng JobId, model và trang được chọn, xác minh nguồn trên server.
- Endpoint `PATCH /api/documents/{id}/ocr` nhận JWT người dùng hoặc header nội bộ `X-Service-Token`.
- `SERVICE_TOKEN` của OCRService phải khớp với `ServiceAuth:OcrServiceToken` của DocumentService.


## Xóa vĩnh viễn công văn đã xóa/hủy (24/10/2026)

Admin dùng `POST /api/documents/{id}/purge` với `reason`, `confirmationTitle` đúng tên và `expectedVersion` hiện hành (GUID0 được chấp nhận nếu khớp dữ liệu cũ). Chỉ áp dụng hồ sơ `IsDeleted`; hồ sơ đang hoạt động phải hủy trước. Giao diện Công văn đã xóa đặt nút Xóa vĩnh viễn cạnh Khôi phục; hồ sơ Cancel chỉ có Xóa vĩnh viễn.

Khóa hàng PostgreSQL, xóa Document/DocumentProcesses/Signatures và thông báo có FK trong một transaction; giữ bản ghi độc lập `DocumentPurgeRecords` chứa người thực hiện, lý do, tên, phiên bản và thời điểm. Dọn các đường dẫn tệp hiện hành/gốc/chỉ đạo sau commit, không xóa tệp còn được hồ sơ khác tham chiếu. Lỗi lưu trữ được lưu bền để worker thử lại mỗi30 giây, trả `filesPendingCleanup=true`. Không tự purge hồ sơ cũ; worker chỉ dọn yêu cầu purge đã được Admin xác nhận. Migration `20261024103037_AddDocumentPurgeRecords` chỉ tạo bảng nhật ký/hàng đợi.


## Quyền tạo công văn (24/10/2026)

Theo xác nhận mới ngày25/10: Admin tạo cả hai chiều; Văn thư chỉ tạo công văn đến; Chuyên viên chỉ tạo công văn đi. Manager/Ban Giám hiệu không được tạo. POST /api/documents kiểm tra vai trò ở server; không tin role/userId gửi từ body. Giao diện trang tạo, menu, danh sách và Dashboard cho phép Admin cùng Văn thư/Chuyên viên.


**Thông tin người tạo (24/10/2026):** DocumentDto trả CreatedById và CreatedByName. Result filter tra tên hiện hành theo ID từ AppUsers, batch một lần cho danh sách; tên trống dùng Username. Không đổi ownership hay dữ liệu công văn để hiển thị tên.

## Hàng đợi OCR công văn (25/10/2026)

- Admin/Văn thư gửi `POST /api/documents/{id}/ocr-jobs` với `{expectedVersion,modelId,pages}`; kiểm tra tài khoản còn hoạt động/quyền hiện hành, công văn, phiên bản, tệp PDF/ảnh và tối đa5 trang. Trả202 sau khi lưu Queued vào PostgreSQL, không giữ HTTP chờ AI. `GET .../ocr-jobs/latest` trả trạng thái gần nhất, không cache. Một công văn chỉ có một tác vụ Queued/Running; không lưu JWT/API key.
- `OcrJobStore.InitializeAsync` tạo bảng vận hành OcrJobs và các index nếu thiếu sau migration chính; dữ liệu UTC. Cần quyền CREATE TABLE/INDEX khi khởi động lần đầu. Không sửa bảng nội dung hoặc chạy lại tác vụ thành công cũ.
- `OcrJobOutbox` chuyển lệnh sang topic Kafka ocr.gemini.requested.v1. `OcrJobWorker` consume topic bằng group document-gemini-ocr-v1; PostgreSQL session advisory lock giữ một tác vụ AI tại một thời điểm và claim nguyên tử đúng JobId nhận từ Kafka. Trước AI kiểm tra lại Admin/Clerk còn hoạt động. Gọi OCRService qua named HttpClient ocr-background, timeout30phút, `OcrService:BaseUrl` và token nội bộ `ServiceAuth:OcrServiceToken`. Docker đã cấu hình URL nội mạng.
- Hoàn tất: khóa tác vụ/công văn, so sánh MinioPath nguồn; chấp nhận sửa metadata trong lúc chờ. Chỉ ghi OcrDataRaw/ConcurrencyVersion, thêm DocumentProcesses action GeminiOcrCompleted, cập nhật Succeeded và một UserNotifications cho đúng người gửi, cùng transaction. Trigger realtime hiện có phát sau commit. Không dùng action UpdateOCR để tránh gửi trùng cho người tạo công văn.
- Lỗi: Failed và thông báo OcrFailed có nội dung/lý do; không ghi đè kết quả cũ và không tự gọi lại AI. Tệp bị thay/công văn bị xóa được báo lỗi thay vì lưu kết quả sai nguồn. Thông báo lỗi không gắn FK hồ sơ để vẫn còn sau purge.
- Tác vụ Queued tồn tại qua khởi động lại. Running bị gián đoạn được chuyển Failed và thông báo khi worker lấy lại lock; không replay một yêu cầu AI có thể đã tính phí. Người dùng tự chọn model/chạy lại. PostgreSQL/Monitoring schema dùng chung với Gateway phải sẵn sàng.
- Kiểm thử OcrJobTests dùng PostgreSQL cô lập qua HAU_OCR_JOB_TEST_POSTGRES, HTTP AI giả; kiểm tra queue/controller, quyền, lock, sửa metadata, thay tệp, lưu kết quả/thông báo nguyên tử, khôi phục và LISTEN nhận sự kiện thật.

GitHub Actions job test có PostgreSQL16 và Kafka3.7.2 riêng, truyền HAU_OCR_JOB_TEST_POSTGRES/HAU_OCR_JOB_TEST_KAFKA; mỗi testcase tạo/xóa schema riêng trên database fixture, không trỏ database ứng dụng. Chạy local cần tự đặt biến này tới database fixture trên localhost; khi không cấu hình, các integration case được báo skipped rõ ràng (kiểm tra định dạng trang vẫn chạy).

## Kafka cho hàng đợi OCR Gemini (25/10/2026)

Luồng hiện tại: POST ocr-jobs → transaction PostgreSQL lưu Queued/lệnh chờ gửi → HTTP202 → OcrJobOutbox publish Kafka → OcrJobWorker consumer → HTTP nội bộ OCRService → transaction lưu kết quả/trạng thái/thông báo → commit Kafka offset. Worker không quét database để lấy tác vụ chạy; chỉ claim JobId nhận từ Kafka. PostgreSQL giữ trạng thái, lệnh chưa publish và chặn chạy trùng.

- Topic mặc định `ocr.gemini.requested.v1`, group `document-gemini-ocr-v1`; cấu hình KafkaSettings:TopicOcrRequested/OcrConsumerGroup/BootstrapServers. Docker có sẵn broker kafka:9092. Worker dùng AdminClient tạo topic1partition/replica1 nếu chưa có, chấp nhận TopicAlreadyExists khi restart; broker cần cho phép tạo topic hoặc topic cần được cấp sẵn. KafkaSettings:Enabled vẫn điều khiển producer document.uploaded cũ; hàng đợi OCR mới luôn cần broker được cấu hình. Không dùng lại topic document.uploaded để tránh trộn consumer Paddle tự động với OCR có model/trang do người dùng chọn.
- Envelope chỉ `{schemaVersion:1,jobId}`, key=jobId; không gửi bytes tệp, API key hoặc JWT. Đường dẫn/model/trang được worker đọc từ bản ghi đã kiểm tra quyền, không tin nội dung người lạ gửi vào Kafka.
- Cột PublishedAt/index outbox thêm bằng ALTER TABLE ADD COLUMN IF NOT EXISTS khi startup. Outbox dùng transaction/row lock SKIP LOCKED, chỉ đánh dấu PublishedAt sau broker ACK (producer EnableIdempotence=true/Acks.All). Nếu broker tạm lỗi, transaction rollback, lệnh vẫn Queued và được gửi lại; API không phải chờ broker. Queued quá5phút chưa được nhận sẽ publish lại an toàn để phục hồi khi message mất do retention/reset topic. Đây là gửi lại message, không tự gọi lại AI.
- Consumer tắt auto commit/auto offset store, MaxPollInterval35phút so với HTTP OCR30phút. Chỉ commit sau khi transaction thành công/lỗi và thông báo đã lưu. Nếu offset commit bị lỗi, message có thể được giao lại; claim WHERE Status=Queued bảo đảm job Running/Succeeded/Failed không gọi AI lần nữa. Message hỏng hoặc JobId không tồn tại được bỏ qua/commit, không gọi AI; envelope hỏng có log cảnh báo. Không tuyên bố Kafka giao đúng một lần.
- PostgreSQL advisory lock tiếp tục giữ một worker AI toàn hệ thống. Khi restart, Running cũ chuyển Failed + thông báo (không replay AI có thể đã tính phí), sau đó consume tiếp Queued từ Kafka. Nếu muốn thay topic/group ở hệ thống có việc chờ, outbox sẽ tái phát Queued sau tối đa5phút.
- Tài liệu đối chiếu cấu hình/offset: https://docs.confluent.io/kafka-clients/dotnet/current/overview.html và https://docs.confluent.io/platform/current/clients/confluent-kafka-dotnet/_site/api/Confluent.Kafka.ProducerConfig.html .
- Kiểm thử Kafka thật dùng topic/group ocr-fixture-<guid> riêng và xóa đúng tài nguyên test sau chạy; HTTP AI giả. Bao phủ outbox→broker→consumer→DB→notification→committed offset, thành công/429, poison envelope và message trùng. Bài kiểm tra outbox giả lập mất kết nối trước publish và mất ACK sau broker nhận message.

Hoàn tất OCR cũng cập nhật realtime cho những tài khoản đang mở cùng công văn: Gateway có trigger theo dõi Documents.OcrDataRaw sau commit, phát DocId tới SSE của người đang xem. Transaction hoàn tất OCR/Kafka offset không đổi; chỉ requester nhận bản ghi thông báo, người xem đọc lại kết quả có xác thực và giữ nguyên quyền OCR. Xem README Gateway/Frontend, cập nhật25/10/2026.

## Bản Word OCR đã hiệu chỉnh (25/10/2026)

`POST /api/documents/{id}/ocr-edited-file?expectedVersion=<guid>` nhận multipart file .docx tối đa50MB. Chỉ Admin/Văn thư đang hoạt động được gửi. Kiểm tra phiên bản, kết quả Gemini, nguồn tệp và tác vụ OCR đang chờ/chạy cả trước lưu object và trong transaction khóa công văn. DOCX phải là ZIP có word/document.xml đúng namespace; cấm DTD, macro, giới hạn số entry/dung lượng giải nén. Tên file tải lên giữ trong metadata; tên object do server tạo.

Bản Word nằm riêng trong MinIO, metadata editedDocument trong OcrDataRaw gồm ID, tên/đường dẫn tệp, SourcePath, người sửa, tên/role lấy từ DB, thời gian UTC và số lần sửa. Không đổi MinioPath/FileName của công văn, không sửa PDF/chữ ký/trạng thái luồng. Cập nhật OCR JSON/ConcurrencyVersion và lịch sử OcrEdited cùng transaction; trigger realtime hiện có cập nhật các tài khoản đang xem. Không thêm thông báo chưa đọc.

`GET .../ocr-edited-file?revisionId=<guid>` trả đúng bytes Word/tên file hiện hành; cùng quyền Admin/Clerk như tải Word OCR trước đây, kiểm tra hồ sơ active/ID bản sửa/nguồn tệp còn khớp. Các role xem chỉ thấy trạng thái người sửa và bản OCR tự động để đối chiếu, không cấp thêm quyền xuất/chỉnh Word.

Chạy lại Gemini trên cùng nguồn giữ bản Word do con người sửa; nguồn tệp mới làm bản sửa cũ hết hiệu lực. Metadata người sửa được server quản lý: endpoint PATCH OCR cũ và JSON từ AI không được tự khai editedDocument.

Bảng vận hành OcrEditedFileCleanup/trigger được khởi tạo idempotent sau migration. Thay bản Word, thay nguồn, hoặc purge sẽ xếp object cũ vào hàng chờ dọn. Worker thử lại mỗi30giây, khóa hồ sơ trước hàng cleanup, giữ tệp còn được tham chiếu (kể cả COMMIT đã thành công nhưng mất phản hồi); lỗi lưu sau upload xếp object chưa gắn vào hàng dọn. Không xóa tệp gốc. Audit giữ các lần sửa; chỉ giữ file Word hiện hành, không có kho file lịch sử mọi phiên bản.

## Tách quyền tạo và trình công văn theo vai trò (25/10/2026)

Văn thư chỉ tạo CongVanDen; Chuyên viên tạo chiều đi (các loại khác CongVanDen tiếp tục dùng nhánh đi hiện có). Admin hỗ trợ tạo cả hai. POST documents tra loại thực từ server và kiểm tra JWT roles, không tin role/hướng do client tự gửi. Người có nhiều vai trò hợp lệ được lấy hợp các quyền tạo.

POST documents/{id}/submit chỉ Admin hoặc Specialist; Specialist phải là người tạo dự thảo, hồ sơ không rõ CreatedById cần Admin đối soát. Service nhận Draft hoặc Rejected để người soạn sửa và trình lại; từ chối nhánh đến. Văn thư dùng incoming-submit để trình BGH chỉ đạo; không dùng submit chiều đi. Nhánh đến và quyền ký/phát hành giữ nguyên.

## Đồng bộ báo cáo mới ngày 26/10/2026

Sửa metadata/tải tệp: Văn thư xử lý chiều đến, Chuyên viên chỉ xử lý chiều đi do mình tạo; Admin hỗ trợ. API truyền DocumentActor từ JWT vào service để kiểm tra lại trên đúng snapshot được ghi; không nhận vai trò/người tạo từ body. Tài khoản thường không đổi chiều để vượt quyền. Chỉ thay tệp khi Draft hoặc chiều đi Rejected; repository chốt cả đường dẫn, phiên bản và giai đoạn bằng cập nhật có điều kiện, tránh ghi đè hồ sơ vừa trình/ký. Lỗi xung đột trả 409 và dọn object mới chưa được gắn.

OCR và Word hiệu chỉnh: Văn thư chỉ được chạy/gửi bản sửa cho công văn đến; Admin hỗ trợ cả hai chiều. Chuyên viên không được chạy OCR. Kiểm tra cả POST ocr-jobs, GET ocr-state, PATCH ocr, upload Word, trước gọi AI và trước lưu kết quả. Bản OCR/Word lịch sử vẫn được xem theo quyền đọc hiện có; không xóa dữ liệu cũ. Worker dùng vai trò đang hoạt động trong DB, không chỉ claims cũ. API ký/phát hành không đổi trong đợt này.

PUT metadata không cho tài khoản thường đổi giữa chiều đến và đi để vượt quyền tạo; Admin vẫn theo các kiểm tra nghiệp vụ hiện có. Không đổi loại hoặc người tạo của dữ liệu cũ.

## Bổ sung OCR cả hai chiều ngày 04/11/2026

Phạm vi hiện hành thay hạn chế chiều đi ở ghi chú ngày 25–26/10: Văn thư chạy OCR và tải Word hiệu chỉnh ở cả hai chiều khi Draft/Rejected. Quyền sửa thông tin/thay tệp nguồn/trình chiều đi vẫn thuộc Chuyên viên là người tạo hoặc Admin hỗ trợ. Chuyên viên không chạy OCR; riêng người tạo chiều đi được tải Word nhận diện/bản sửa hiện hành để hoàn thiện nội dung bên ngoài.

Tệp chính .docx là văn bản hoàn chỉnh, không hiện OCR và bị chặn trước gọi AI. Ảnh/PDF dùng model và tối đa 5 trang đã chọn; upload không tự OCR. Văn thư không gửi OCR/Word hiệu chỉnh sau khi chuyển duyệt/ký/phát hành; kiểm tra lại trước AI và lúc lưu, không ghi đè tệp/chữ ký nguồn. Admin giữ ngoại lệ hỗ trợ đã thống nhất. Chưa tự chuyển Word sang PDF hoặc phân loại PDF có lớp chữ.

GET /api/documents/{id}/ocr-word?expectedVersion=... cho Admin/Clerk hoặc Specialist là người tạo chiều đi, lấy layout từ server và chặn phiên bản cũ. Có bản Word hiệu chỉnh thì trả nguyên bytes/tên; nếu chưa có, dựng DOCX qua POST /api/ocr/internal/export-layout với X-Service-Token, không gọi AI. Route nội bộ từ chối token sai/trống; không mở public export-layout cho Specialist. Trạng thái/nhãn và kết quả realtime giữ nguyên. Thiết kế hiện hành: tai_lieu/06_thiet_ke_he_thong/PHAN_QUYEN_TAO_VA_TRINH_CONG_VAN.md.
