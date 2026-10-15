# Gemini 3.8 Flash — một request tái tạo Word

Ngày 12/10/2026, dùng cùng ảnh kế hoạch thực tế và bằng chứng Paddle đã lưu. Script: `tools/test_gemini_single_shot_word.py`.

Prompt chuyên biệt cho số hóa công văn, quy định nguồn ảnh ưu tiên, không bịa nội dung, bảo toàn vùng song song, đếm hàng trống, kiểu boolean, độ rộng tương đối và newline. Schema không chứa nội dung hoặc số hàng đúng của mẫu. Model tự kiểm tra nội bộ; không có vòng tool calling hoặc sửa tay.

- Model: `gemini-3.8-flash`, thinking medium, temperature 0, JSON Schema, tối đa 12000 output tokens.
- Đúng **một POST generateContent**, không retry. Sau 3,40 giây nhận HTTP 503 / UNAVAILABLE. Google trả thông báo model đang có nhu cầu sử dụng cao.
- **Chưa có layout, DOCX hoặc render. Chưa đánh giá được chất lượng model.** Không dùng lỗi dịch vụ này để kết luận model OCR kém.
- Giữ nguyên prompt, schema, generation config, bằng chứng Paddle, response và metadata. Khóa không được ghi vào output; bản khóa tạm trong container đã xóa, môi trường thử đã dừng.
- Script kiểm tra cú pháp đạt. Không đổi endpoint hoặc deploy ứng dụng.

Tham chiếu: [model chính thức](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash), [structured outputs](https://ai.google.dev/gemini-api/docs/structured-output).
