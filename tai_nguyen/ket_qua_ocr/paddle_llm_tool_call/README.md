# Thử PaddleOCR + LLM bằng native tool calling — 11/10/2026

## Kết quả nguyên trạng

- Đầu vào: `../../mau_cong_van/ke-hoach-thuc-te.jpg`, SHA-256 `3ac3abe4aa4c1c45917b27c49694c9718094b40955f42c62a4e073699dc72e52`.
- PaddleOCR chạy thật, nhận 30 dòng; bằng chứng tại `paddle_evidence.json`.
- Model LM Studio local: `gemma4-12b-qat-uncensored-hauhaucs-balanced`.
- Lượt agent hoàn tất trong 402,49 giây, không bao gồm chạy Paddle và các thử nghiệm schema trước đó.
- 24 native tool calls, gồm hai lần `render_document`, một lần `clear_draft`, kết thúc bằng `finish_document(iteration=2)`. Model được gửi ảnh render sau mỗi lần xuất để tự xem; không cung cấp nội dung đã đối chiếu thủ công.
- File cuối: [ke_hoach_model_tool_call.docx](ke_hoach_model_tool_call.docx). Trùng SHA-256 với `iteration-2/document.docx`; không sửa nội dung hoặc định dạng sau khi model chọn.
- Render cuối: [PNG](iteration-2/render/page-1.png), [PDF](iteration-2/render/document.pdf). Một trang, có văn bản và bảng Word thật, không nhúng ảnh chụp.

## Bằng chứng tool calling

`turn-1.json` đến `turn-24.json` lưu message với `tool_calls`, ID, tên hàm và chuỗi arguments; finish reason là `tool_calls`. `tool_events.json` lưu các tham số đã parse cùng kết quả thực thi. Đây là tool call của API, không phải suy diễn từ JSON hoặc lời văn trong câu trả lời. `metadata.json` ghi `manual_content_edits: false`.

Thử nghiệm ban đầu dùng một tool nhận toàn bộ layout gặp cấu trúc cột lồng không hợp lệ. Chuyển sang các tool nhỏ để thêm đoạn, cột và bảng; không vá nội dung model. Vòng agent hoàn tất có 24/24 thao tác trả `ok: true`, nhưng điều này chỉ xác nhận thao tác được thực thi, không xác nhận nội dung đúng hay tham số hoàn toàn tuân thủ schema.

## Giới hạn quan sát được

- Tiêu đề sai, cụm “chữ ký số” bị thiếu chữ “ký”, thiếu dòng ngày ký; vị trí số trang và tỷ lệ bảng chưa giống ảnh.
- Model gửi `blank_rows: 8` đồng thời tám `rows` rỗng. Bộ xuất cộng hai phần, tạo 16 hàng thân thay vì 7 hàng trong ảnh.
- Model gửi chuỗi chứa backslash-n/backslash-t thay vì xuống dòng/tab thật; các ký tự này còn trong tài liệu.
- Tham số `bold` được trả thành object `{"true": false}` thay vì boolean. Bộ xuất dùng `bool(object)` nên vẫn in đậm. Đây là hạn chế của cả tham số model và kiểm tra kiểu trong adapter; không quy toàn bộ lỗi định dạng cho nhận diện ảnh.
- Đã xem render cả hai bản; bản tự dựng lại vẫn còn lỗi. Không sửa tay theo yêu cầu người dùng. Chưa tích hợp pipeline thử nghiệm vào endpoint production.

Script: `tools/test_paddle_llm_word_agent.py`, dùng streaming helper `tools/test_paddle_llm_tools.py` và bộ xuất `OCRService/app/services/layout_exporter.py`. Chạy trong môi trường OCR có python-docx, httpx, Pillow và renderer `render_docx.py` với LibreOffice/Times New Roman. Truyền `--image`, `--paddle-json`, `--output-dir` mới, `--renderer`, `--model`; mặc định API trong Docker là `http://host.docker.internal:1234/v1`, tối đa 32 lượt.
