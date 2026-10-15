# Gemini 3.5 Flash Lite và PaddleOCR — 12/10/2026

Model Google AI Studio: `gemini-3.5-flash-lite`, API model listing xác nhận khả dụng. Dùng Google OpenAI-compatible endpoint, reasoning_effort=low, temperature=0; cùng ảnh và 30 dòng Paddle cũ để đối chiếu local. Không đưa bản Word đã sửa tay hoặc số hàng đúng từ PP-Structure vào prompt.

## Kết quả nguyên trạng

- Model hoàn tất bằng finish_document ở lượt 60, chọn iteration 6. [Word model chọn](ke_hoach_model_tool_call.docx) trùng SHA-256 với `iteration-6/document.docx`, không sửa nội dung/định dạng thủ công.
- 60 tool calls: 51 thao tác thành công, 9 lỗi; 6 lần yêu cầu render nhưng chỉ 3 render thành công. Model tự clear_draft 3 lần. Số iteration gồm các lần lỗi, không có sáu bản render thành công.
- Metadata ghi 427,54 giây; có chờ quota và gián đoạn sửa môi trường/resume, không phải benchmark tốc độ model. Usage cộng từ các response đã lưu: prompt 471190, completion 3751, tổng 474941 token; không gồm lượt adapter lỗi đầu hoặc request 429 không có usage. Chưa tính chi phí tiền.
- Word cuối một trang A4, chữ và bảng sửa được, không nhúng ảnh. Đầu trang/chữ ký song song, tiêu đề/đề tài/ngày/tên đọc rõ hơn các bản Paddle-only của cùng ảnh, đã hết ký tự backslash-n/backslash-t hiện trong render.
- **Chưa giống ảnh hoàn toàn:** bảng có 6 hàng thân trống thay vì 7, thiếu số trang 246, ghi chú thiếu từ “này”, cỡ chữ/chiều cao bảng và tỷ lệ cột khác ảnh. Chưa có CER/WER hoặc nghiệm thu nhiều tài liệu; không đánh dấu yêu cầu tái tạo giống ảnh là Pass.

## Lỗi và cách khôi phục

- `adapter_failed_attempt/`: phản hồi đầu có nhiều tool calls không kèm index; parser cũ gộp sai và API lượt tiếp theo trả 400. Đã sửa phân biệt call theo ID khi thiếu index, giữ thought_signature nguyên trạng. Đây là lỗi adapter, không phải kết quả nhận diện model.
- Lượt sau model gửi widths=300 vượt giới hạn bộ xuất, rồi tự dựng lại. Virtualenv mới thiếu pdf2image khiến hai render lỗi; đã cài dependency và gửi thông báo môi trường đã sửa để model gọi render lại. Không sửa tham số/nội dung model.
- API báo 429 với quota free-tier request limit 15 và retry delay. Runner thêm chờ/thử lại theo API, giữ log retries; khôi phục conversation từ các response/tool events, không gọi lại các thao tác đã thực thi.
- Do gián đoạn, tiếp tục cùng lượt từ 15, 33, 41 và 57; tăng trần lượt lên 64, kết thúc ở 60. Thời gian includes khoảng khôi phục, không so trực tiếp với lượt local 32 lần.
- Đã kiểm tra parser bằng fixture: calls có index/phân mảnh, nhiều calls thiếu index và bảo toàn signature; py_compile đạt. Đã xem render bản cuối và kiểm tra DOCX/hash/cấu trúc.

## Cấu hình và bằng chứng

Khóa lưu ở `.env.gemini` tại root, Git bỏ qua; không có trong log/metadata. Bản tạm trong container đã xóa. Không ghi giá trị khóa trong tài liệu hoặc lệnh mẫu.

Script `tools/test_paddle_llm_word_agent.py` hỗ trợ `--api-key-file`, `--reasoning-effort low`, `--resume` và endpoint Google. `tools/test_paddle_llm_tools.py` lưu SSE nguyên bản, usage, ID/signature và retries. Kết quả gồm turn JSON/JSONL, tool_events, metadata, verification, Paddle evidence, Word trung gian và render; resume notes chỉ thông báo sửa hạ tầng, không cung cấp nội dung đã đối chiếu.

Ảnh và bằng chứng OCR đã gửi Google theo yêu cầu người dùng. Đây là thử nghiệm; chưa cấu hình endpoint ứng dụng sử dụng Gemini và chưa redeploy frontend/OCRService. Container môi trường thử được dừng sau khi lưu kết quả, không tạo lại hau-ocr-render.

Nguồn: [model chính thức](https://ai.google.dev/gemini-api/docs/models/gemini-3.5-flash-lite), [OpenAI compatibility](https://ai.google.dev/gemini-api/docs/openai), [thought signatures](https://ai.google.dev/gemini-api/docs/generate-content/thought-signatures).
