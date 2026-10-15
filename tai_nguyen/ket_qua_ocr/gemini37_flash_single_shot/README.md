# Gemini 3.7 Flash — Word từ một request

Ngày 12/10/2026. Gọi tên `gemini-3.7-flash` theo yêu cầu, nhưng Google trả `modelVersion=gemini-3.8-flash`. Đây không phải phép thử độc lập của trọng số 3.7: [tài liệu chính thức](https://ai.google.dev/gemini-api/docs/models/gemini-3.7-flash) xác nhận tự định tuyến sang 3.8.

- Cùng ảnh và bằng chứng Paddle; cùng prompt/schema lượt 3.8 trước, thinking medium/temperature 0. Không đưa số hàng đúng vào prompt, không sửa tay.
- Một POST generateContent duy nhất, HTTP 200, không retry/tool loop. Sinh 52,93 giây; tổng gồm xuất/render local 54,49 giây. Usage 9472 tokens theo API, gồm 2222 thinking tokens; chưa quy đổi chi phí.
- Word: `ke_hoach_gemini-3_7-flash_mot_lan.docx`, một trang, chữ/bảng editable, không nhúng ảnh. Giữ số 246, tiêu đề, thông tin sinh viên, đề tài, ngày và hai vùng chữ ký. Không có literal backslash-n trong Word.
- **Chưa đạt giống ảnh hoàn toàn:** bảng 4 cột nhưng 8 hàng thân trống thay vì 7; tỷ lệ bảng/cỡ chữ/giãn cách khác ảnh, đoạn đề tài chưa giữ in đậm như nguồn, quốc hiệu “HOÀ” khác “HÒA” trong ảnh. Dấu đỏ không tái tạo trong body, được model ghi ở uncertain_fields. Chưa đo CER/WER hoặc đánh giá đa tài liệu.
- Đã mở render một trang và đối chiếu ảnh gốc; giữ nguyên response/layout/Word. SHA-256 Word: `53a2814cb0dc1294e02ad147277d8e373572e99290710616f516de1393b03575`.
- Đã quét output, xóa khóa tạm và dừng container thử. Không thay đổi endpoint/deploy ứng dụng. Testcase 057, nhật ký 66.
