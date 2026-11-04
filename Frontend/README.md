# Frontend

Frontend là Blazor WebAssembly app cho hệ thống HAU DigitalSign OCR.

## Chạy local

Đảm bảo API Gateway đang chạy ở:

```text
http://localhost:5000
```

Chạy frontend:

```bash
dotnet run --project Frontend/HauDocumentApp.csproj
```

Port dev:

```text
http://localhost:5227
```

## Chạy bằng Docker

Frontend được publish thành static files và serve bằng Nginx:

```bash
docker compose up -d frontend
```

Hoặc chạy toàn bộ hệ thống:

```bash
docker compose up -d
```

URL:

```text
http://localhost:5227
```

Dockerfile của frontend đã cài `python3` và `dotnet workload install wasm-tools` trong build stage để `dotnet publish` chạy được native WebAssembly optimization.

Nếu publish frontend trực tiếp trên máy host, nên cài workload tương ứng:

```bash
dotnet workload install wasm-tools
```

## Cấu hình API Gateway

`Frontend/Program.cs` hiện cấu hình:

```csharp
BaseAddress = new Uri("http://localhost:5000/")
```

Nếu người dùng mở frontend từ máy khác trong LAN, cần đổi `localhost` thành IP/domain của máy deploy trước khi build frontend, ví dụ:

```csharp
BaseAddress = new Uri("http://192.168.1.50:5000/")
```

Sau đó build lại:

```bash
docker compose build frontend
docker compose up -d frontend
```

## Ghi chú hiện tại

- JWT được lưu trong localStorage.
- Frontend đã có các màn hình auth, dashboard, admin, documents, signatures.
- Có màn hình Quét OCR và xuất Word, chọn trang và model Gemini.
- DTO ký số frontend đã đồng bộ với backend SignService: gửi `DocId`, `SignerId`, `SignerName`, `Reason` và map thao tác UI sang endpoint ký phù hợp.


## Tiến độ tải tệp (24/10/2026)

Tạo công văn, tải/thay tệp ở chi tiết và upload PDF/ảnh ở trang Quét OCR hiển thị popup góc trên phải. Phần trăm lấy từ XMLHttpRequest.upload.onprogress, không mô phỏng theo thời gian. Gửi đủ dữ liệu chuyển sang chờ máy chủ xử lý; chỉ xác nhận thành công sau HTTP2xx, tự đóng sau5 giây. Lỗi giữ popup cho người dùng đóng/thử lại từ màn hình tải tệp.

BrowserUploadService chụp tham chiếu File đầu vào, gửi FormData trực tiếp từ trình duyệt với JWT hợp lệ và giữ response nhị phân/headers. Không đọc toàn bộ tệp vào bộ nhớ .NET để theo dõi tiến độ; giải phóng tham chiếu sau request. Không tự gửi lại upload khi401. Luồng gốc giữ giới hạn50 MiB/công văn và25 MiB/OCR; khóa chọn tệp khi đang gửi. Popup tồn tại qua điều hướng SPA, rộng tối đa340px và tự thu trên điện thoại.


**Chống upload bị đứng (24/10/2026):** Khi45 giây không có tiến triển gửi dữ liệu, XHR bị hủy và giao diện nhận lỗi để mở lại thao tác; sau khi gửi xong dùng timeout xử lý riêng3 phút. Hoàn tất response không chờ callback giao diện tiến độ để tránh khóa Promise. Có thông báo riêng cho lỗi mạng, ngừng tải và quá thời gian xử lý; không tự replay OCR/upload. Popup phân biệt lỗi xử lý máy chủ5xx sau khi tệp đã gửi với lỗi truyền dữ liệu.


**Xem trước ảnh đính kèm (24/10/2026):** Trang tạo hiện ảnh ngay khi chọn (chưa tải lên). Trang chi tiết có khung Chưa có tệp khi MinioPath trống; chọn ảnh hiện ngay previewblob và nhãn Chưa lưu trong lúc upload. Lỗi giữ preview chưa lưu; thành công thay bằng ảnh tải từ máy chủ và giữ được sau reload. Giải phóng URL khi đổi tệp, đổi công văn hoặc rời trang. Sau mỗi lần upload ở chi tiết resetinput để chọn lại cùng tệp vẫn phát sinh onchange; không resetinputOCR đang cần dùng để gửi lại/model khác.


**Cuộn xem trước trên điện thoại (25/10/2026):** Giữ canvas và texture taskbar khi chỉ cuộn hoặc thanh địa chỉ làm đổi chiều cao viewport; chỉ cấp lại buffer khi đổi chiều rộng/DPR, cập nhật nhãn khi đổi theme/hướng/số tab. Texture nền dùng độ phân giải theo CSS pixel để tránh tăng bộ nhớ trên điện thoại DPI cao. Khung chi tiết/preview mobile dùng nền tĩnh không backdrop-blur; giữ nền trang và hiệu ứng taskbar. Kiểm tra vuốt touch, resize chiều cao, xoay màn hình, ảnh chỉ tải một lần bằng tests/mobile-preview-scroll.spec.cjs.


## Ảnh Ultra HDR trên điện thoại (25/10/2026)

ADB trên điện thoại thật xác nhận JPEG có HDR gain map làm Android bật/tắt lớp HDR khi ảnh vào/ra viewport. Người dùng xác nhận hết nhấp nháy khi thay riêng phần xem trước bằng SDR. Tham khảo cơ chế hiển thị: https://developer.android.com/media/grow/ultra-hdr/display.

`documentFiles.createImagePreview` giải mã vào canvas sRGB 8-bit và xuất PNG SDR, cạnh dài tối đa2048px cho chi tiết; `previewFiles` dùng1600px cho ảnh vừa chọn ở trang tạo/chi tiết/OCR. Giữ alpha cho PNG/WebP, giải phóng canvas và URL nguồn tạm; không hiển thị ảnh HDR gốc làm fallback khi giải mã lỗi. `Detail.razor` giữ URL gốc riêng cho tải xuống/mở tab mới, URL xem trước riêng cho `<img>`, giải phóng khi tải lại/rời trang. File upload, dữ liệu lưu trữ, OCR và ký số vẫn dùng tệp gốc; không lưu thumbnail vào MinIO. PDF/Word không qua bước chuyển ảnh.

Kiểm tra hồi quy preview xác minh header PNG và URL riêng, byte File được native FormData sử dụng và byte tệp gốc dùng tải xuống. Kiểm tra thực tế sau reload điện thoại: ảnh gốc5040489bytes, SHA-256 không đổi; previewPNG1536×2048; SurfaceFlinger giữ lớp HDR ở0 sau các lượt vuốt. Không chỉ dựa vào CSS dynamic-range-limit: máy thử hỗ trợ cú pháp nhưng vẫn giữ lớp HDR khi dùng ảnh gốc.


## OCR Gemini trong chi tiết công văn (25/10/2026)

Văn thư/Admin bấm **Chạy OCR** trong chi tiết để chọn model và tối đa5 trang từ chính tệp đã đính kèm công văn; không có ô chọn/tải lại tệp. Khi MinioPath trống, báo tải tệp lên trước. Backend đọc nguồn từ MinIO sau khi kiểm tra JWT/trạng thái/ExpectedVersion. Trang `/documents/ocr-scan` độc lập vẫn cho chọn một PDF/nhiều ảnh và tải Word như trước; ảnh xem trước vẫn qua bản SDR.

Chi tiết gọi `POST /api/ocr/gemini/documents/{id}/input-info` để đếm trang, sau đó **Gửi yêu cầu OCR** qua `POST /api/documents/{id}/ocr-jobs` với expectedVersion/modelId/pages. API trả **202 Accepted** cùng mã tác vụ sau khi lưu PostgreSQL; đóng hộp chọn trang và cho phép sửa thông tin, tải tệp hoặc rời trang. Một công văn chỉ có một tác vụ Queued/Running, không tự chạy lại AI khi lỗi. Các yêu cầu trùng dùng cùng mã tác vụ đang hoạt động.

DocumentService outbox gửi JobId qua Kafka; worker consume topic riêng rồi gọi nội bộ OCRService bằng service token, giữ pipeline Paddle evidence + Gemini. Backend tự lưu `OcrDataRaw`, lịch sử và thông báo người đã yêu cầu trong cùng transaction; trình duyệt không còn đợi nhận diện hoặc PATCH kết quả. Chỉ cập nhật nội dung OCR/phiên bản, giữ các thông tin công văn được sửa trong lúc chờ. Nếu tệp bị thay hoặc công văn bị xóa, báo lỗi và không gắn kết quả vào nguồn mới.

Trạng thái khôi phục bằng `GET /api/documents/{id}/ocr-jobs/latest` sau reload. Khi ở trang chi tiết, SSE thông báo và kiểm tra trạng thái mỗi5 giây cập nhật kết quả tự động. Khi đã rời trang, chuông/trung tâm thông báo nhận **OCR hoàn tất** hoặc **OCR thất bại**; bấm thông báo đánh dấu đã đọc và mở công văn/kết quả. Tác vụ đang chạy bị gián đoạn khi backend khởi động lại được báo lỗi; tác vụ đang xếp hàng vẫn còn. Không tự gọi lại AI vì có thể đã phát sinh chi phí. Thông báo được lưu để đọc lại; không phải Web Push khi đóng trình duyệt. Trang quét độc lập/tải Word vẫn dùng luồng đồng bộ cũ.

`GeminiOcrResultView`/`GeminiLayoutBlock` hiển thị văn bản, định dạng chữ, cột, bảng và dòng trống bằng Razor mã hóa nội dung; không render HTML do model cung cấp. Kết quả đã lưu hiện lại sau reload và tại đường dẫn OCR cũ. Giao diện bỏ trường bóc tách/JSON/dòng Paddle cũ; dữ liệu cũ không bị xóa tự động. Chuyên viên xem kết quả, không có nút chạy OCR hoặc tải Word OCR để sửa.

**Tải Word** gọi `POST /api/ocr/gemini/export-layout` với layout đã nhận diện; chỉ dựng DOCX, không gọi Google/Paddle lần nữa. Không lưu DOCX/base64 vào database hoặc tạo object mới trong MinIO. Giới hạn5 trang,500 khối,10000 ô bảng/2000 đoạn và payload2MB khi xuất. PDF/Word gốc và luồng ký không thay đổi. Cần tải lại trang sau cập nhật Docker.


## Thời gian Việt Nam (25/10/2026)

Database/API lưu thời điểm theo UTC (`timestamptz`), không đổi đồng hồ container hoặc cộng7 giờ vào dữ liệu đã lưu. Lịch sử xử lý và chữ ký trước đây định dạng trực tiếp UTC nên lệch7 giờ so với ngày tạo. `VietnamTime.Format` thống nhất hiển thị UTC+7 ở ngày tạo công văn/người dùng, lịch sử, chữ ký/chứng thư, thùng rác, Gemini, nhật ký và thông báo, không phụ thuộc múi giờ thiết bị.

Hàm dùng DateTimeOffset.ToOffset(+07:00); DateTime Kind.Unspecified từ dữ liệu API cũ được hiểu là UTC. Timestamp có offset sẵn được chuyển đúng một lần. Ngày ban hành DateOnly vẫn giữ ngày người dùng nhập; xác thực/thời hạn token và khoảng lọc ngày nhật ký không đổi. Test `tests/vietnam-time.spec.cjs` kiểm tra UTC/America/Los_Angeles, desktop/mobile, qua mốc nửa đêm và timestamp Z/+07/không hậu tố.

## Người khác đang xem cũng nhận kết quả OCR realtime (25/10/2026)

Chi tiết công văn đăng ký DocId trên NotificationRealtimeService qua SSE hiện có. Khi Văn thư/Admin OCR xong và backend lưu kết quả, Chuyên viên/Trưởng đơn vị/Ban Giám hiệu đang xem cùng công văn tự đọc lại nội dung đã lưu. Không cần họ có tác vụ OCR đang chạy hoặc quyền chạy OCR; các nút chạy/tải Word để sửa vẫn theo quyền hiện tại.

Chỉ tải lại thông tin/kết quả khi documentVersion khác, giữ URL ảnh/PDF đang xem và vị trí cuộn; không tự cuộn xuống hoặc tải lại toàn trang. Báo nhẹ Đã cập nhật kết quả OCR của công văn. Nếu đang gửi một thao tác, cập nhật được chờ đến khi thao tác kết thúc. Thay nguồn tệp mới mới tải lại bản xem trước. Nối lại stream lấy snapshot hiện hành; đổi công văn/rời trang hủy đăng ký cũ, bỏ qua callback sai DocId. Snapshot báo hồ sơ không còn truy cập được sẽ ẩn nội dung cũ.

Đây là cập nhật nội dung cho người đang xem; không tự thêm thông báo chưa đọc cho tất cả vai trò. Thông báo OCR hoàn tất/thất bại trong trung tâm vẫn thuộc người yêu cầu. Test document-ocr-viewers-realtime.spec.cjs kiểm tra3vai trò đồng thời ở1366/300px, giữ preview/cuộn/quyền, không tăng badge, reconnect và đổi hồ sơ.

## Tải lên Word OCR đã chỉnh sửa (25/10/2026)

Trong kết quả OCR, Admin/Văn thư có nút Tải lên bản chỉnh sửa cạnh Tải Word. Chọn .docx tối đa50MB; dùng native upload/progress popup hiện có. Chưa có kết quả hoặc đang OCR không cho gửi. API kiểm tra quyền/phiên bản/tệp gốc, lỗi không đánh dấu đã sửa.

Sau lưu hiển thị Đã chỉnh sửa bởi Văn thư/Quản trị viên <tên>, thời gian Việt Nam, tên tệp và lần sửa. Tải Word lấy file đã tải lên với đúng tên; không gọi AI hay dựng lại từ JSON cũ. Bản nhận diện tự động nằm trong mục Nội dung để tránh nhầm với bản Word đã sửa; ứng dụng chưa render nội dung Word mới thành trình soạn thảo inline. Metadata và nhãn được lưu qua reload, cập nhật qua SSE cho người đang xem. Chuyên viên/lãnh đạo vẫn giữ quyền đọc hiện tại, không thêm nút tải lên/xuất Word.

### Nhãn và giãn dòng kết quả OCR (25/10/2026)

Khung mang tên Kết quả OCR, ẩn tên model khỏi phần thông tin hiển thị; model vẫn lưu để theo dõi kỹ thuật. Mục đóng/mở thống nhất tên Nội dung và dùng nút outline nhỏ có chevron cùng mẫu nút khác. Khi có Word đã sửa, ghi chú đối chiếu nằm trong phần mở rộng. Giãn dòng văn bản OCR tăng2px so với hệ số1.4, ô bảng/thông tin/hướng dẫn dùng hệ số1.5 cộng2px. Chỉ thay giao diện, không đổi bố cục xuất Word hoặc dữ liệu.

## Quyền tạo/trình theo hai chiều (25/10/2026)

Form tạo lọc Công văn đến cho Clerk, nhánh đi cho Specialist; Admin thấy cả hai. Công văn đi Draft hoặc Rejected chỉ hiện Nộp trình duyệt với Specialist là người tạo hoặc Admin hỗ trợ. Công văn đến của Clerk vẫn dùng Trình BGH chỉ đạo. Form sửa của tài khoản thường chỉ liệt kê loại cùng chiều; dữ liệu cũ không tự chuyển loại. Gửi submit chỉ báo thành công nếu API trả2xx.


## Đồng bộ luồng và quyền theo báo cáo ngày 26/10/2026

Văn thư chỉ sửa/tải tệp, chạy OCR và tải Word hiệu chỉnh cho chiều đến; ở chiều đi giữ phát hành/lưu trữ. Chuyên viên chỉ sửa/tải và trình chiều đi do mình tạo; bản bị trả sửa (Rejected) có nút tải tệp và trình lại. Admin giữ quyền hỗ trợ. Các nút sửa/tải/OCR/Word hiệu chỉnh được kiểm tra cả trong chi tiết và trang kết quả riêng; backend kiểm tra lại, không chỉ ẩn nút.

Upload chỉ lưu tệp; OCR cần chọn model và tối đa 5 trang rồi gửi tác vụ nền. Xem kết quả và cập nhật realtime không cấp quyền OCR. Không đổi nền, bố cục hoặc thao tác ký/phát hành trong đợt này.
