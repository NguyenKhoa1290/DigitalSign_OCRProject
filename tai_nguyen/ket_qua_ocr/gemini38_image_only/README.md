# Gemini trực tiếp ảnh — không Paddle

Ngày 12/10/2026, theo yêu cầu thử không gọi Paddle. Chạy `tools/test_gemini_single_shot_word.py` với model `gemini-3.8-flash`, không truyền `--paddle-json`.

- Chỉ gửi ảnh gốc, prompt và schema; không chạy Paddle, không đọc/gửi kết quả OCR đã lưu. Không gửi Word hoặc layout của lượt trước. Không sửa tay.
- Một POST generateContent, không retry; thinking medium/temperature 0/maxOutputTokens 12000 như lượt trước. Prompt chỉ thay chính sách bằng chứng thành Paddle tùy chọn, user input xác nhận không có bằng chứng Paddle.
- Sau 2,41 giây nhận HTTP 503 UNAVAILABLE. Google thông báo model đang có nhu cầu sử dụng cao; giữ response nguyên trạng.
- **Chưa có layout/Word/render, chưa đánh giá được chất lượng chế độ chỉ ảnh hoặc so sánh với lượt có Paddle.** Không coi lỗi dịch vụ là bằng chứng Paddle cần thiết hay không cần thiết.
- Metadata ghi input_mode=image_only, generation_requests=1, retries=0. Source SHA-256 trùng ảnh các lượt trước. Output không có paddle_evidence.json.
- Khóa tạm đã xóa, container thử đã dừng; không thay service/endpoint/deploy. Testcase 058, nhật ký 68.
