# Thử Google Gemma 4 31B — 12/10/2026

Người dùng yêu cầu Gemma 4 32B. API listing và tài liệu Google cung cấp bản chính thức `gemma-4-31b-it` (31B), đã thông báo tên thực tế trước khi chạy. LM Studio localhost không kết nối được; thử qua Google AI Studio bằng khóa đã lưu ở env local bị Git bỏ qua.

## Kết quả model chọn

[Word nguyên trạng](native_matched_ids/ke_hoach_model_tool_call.docx) là iteration 3 được model chọn bằng finish_document. Một trang A4, văn bản/bảng sửa được, không nhúng ảnh chụp; hash trùng bản model chọn. Đã kiểm tra PNG và cấu trúc DOCX, không sửa chữ hoặc bố cục thủ công.

- Có số trang 246, nội dung đề tài, ngày, ghi chú có từ “này”, các vùng header/chữ ký song song.
- Bảng 4 cột nhưng **10 hàng thân trống thay vì 7**. Đầu trang, tiêu đề và chữ ký còn ký tự backslash-n literal; cỡ chữ, khoảng ký và tỷ lệ bảng chưa giống ảnh. Chưa đạt yêu cầu tái tạo giống ảnh.
- Kết quả chỉ trên một ảnh; không phải benchmark CER/WER hoặc kết luận model tốt/xấu trên mọi tài liệu. Interface cuối là tool nhận toàn bộ layout, khác tool thêm từng khối của lượt Gemini trước, nên không so trực tiếp số lượt/tốc độ để xếp hạng model.

## Các lượt và lỗi đã lưu

1. `per_block/`: cùng ảnh và 30 dòng Paddle của các lượt trước, temperature=0, reasoning minimal. Có 21 tool calls đã ghi, 20 thao tác thành công, một render lỗi do widths=250 vượt giới hạn bộ xuất. Model tự dựng lại; dừng lượt này do nhiều lỗi 500/quota và số request lớn, không có Word được render thành công.
2. `full_layout_compat/`: thử gửi layout trong một tool call qua OpenAI-compatible endpoint; các request bị 500 và có 429, không tạo được Word.
3. `native_schema_rejected/`: API generateContent gốc từ chối schema array lồng thiếu items, HTTP 400. Đây là lỗi schema adapter; đã sửa khai báo đầy đủ page/paragraph/columns/rows/style, không sửa nội dung model.
4. `native_validated/`: schema đã sửa; sau các retry 500, model trả functionCall render_word, tạo bản hai trang. Adapter lúc này chưa echo functionResponse.id; giữ lượt riêng, không dùng để kết luận khả năng tự hoàn tất của model. Chữ ký tràn trang 2, header gom vào một cột, bảng 10 hàng.
5. `native_matched_ids/`: sửa echo ID đúng tài liệu Google, kiểm tra bằng fixture không gọi mạng. Replay nguyên native tool call đầu từ lượt 4, không sửa arguments, rồi gửi render cho model. Model tự dựng lại thành một trang và chọn iteration 3. Có 4 tool calls thực thi gồm một replay; 3 calls mới được API sinh, thêm một response không có tool call. 353,31 giây của lượt cuối gồm replay, retry 500 và render; không gồm các lượt lỗi trước. Không coi đây là thời gian inference thuần.

Các lỗi 500/503/quota và lỗi client/schema đều được phân biệt. Có request nhận 500 ngay khi chưa gửi kết quả tool; không quy toàn bộ lỗi dịch vụ cho adapter hoặc model. Các retry có giới hạn, không tăng quota hoặc đổi model ngầm.

## Môi trường và bằng chứng

Ảnh gốc cùng SHA-256 `3ac3abe4aa4c1c45917b27c49694c9718094b40955f42c62a4e073699dc72e52`, bằng chứng Paddle 30 dòng; không đưa Word đã sửa tay hoặc số hàng đúng từ PP-Structure vào model. Script `tools/test_paddle_llm_word_agent.py`, `tools/test_paddle_llm_tools.py`, `tools/test_google_native_word.py`. Tool schema, native response/functionCall, call arguments/result, retry, layout, DOCX, render và metadata lưu theo từng lượt. `native_matched_ids/verification.json` xác nhận hash, hình nhúng và số hàng/cột.

Đã xem bản hai trang đầu và các bản một trang sau; kiểm tra py_compile và fixture ghép functionResponse.id/call ID đạt. Khóa không có trong output, đã xóa bản tạm khỏi container; khóa gốc vẫn ở `.env.gemini` bị Git bỏ qua. Container thử dừng sau kiểm tra; không khởi tạo lại hau-ocr-render, không đổi endpoint ứng dụng, provider mặc định hay deploy stack.

Nguồn: [Gemma trên Gemini API](https://ai.google.dev/gemma/docs/core/gemma_on_gemini_api), [quy định FunctionCall/FunctionResponse ID](https://ai.google.dev/api/generate-content.md). Gemma dùng minimal để tắt thinking theo API, khác reasoning low của Gemini trước.
