# Prompt tiếng Việt + cấu trúc bảng Paddle

Ngày 12/10/2026 theo yêu cầu thử lại kèm Paddle và xác định 7 hay 8 hàng.

## Dữ liệu Paddle thực tế

Đọc lại PP-StructureV3 đã chạy trên cùng ảnh, không chạy inference Paddle mới. Cả native/OCRv5/OCRv6 đều có HTML **8 hàng tổng: hàng đầu có 4 nhãn cột, 7 hàng sau trống**. Vì vậy có 7 hàng thân trống, không phải 8 hàng thân. `paddle_table_counts.json` chứa các ô HTML được parse và hash nguồn.

`tools/prepare_paddle_layout_evidence.py` tạo đầu vào từ kết quả native: 32 dòng OCR nguyên trạng, HTML bảng, tọa độ ô, số hàng HTML được tính tự động và hash nguồn/ảnh. Không nhập đáp án kiểm thử vào prompt, không sửa chữ Paddle. Các lượt trước chỉ dùng 30 dòng OCR cũ, chưa cung cấp HTML bảng; lần này có cấu trúc bảng thực tế.

## Hai request Gemini

- Dùng ảnh và `paddle_input.json`, prompt tiếng Việt v3, Gemini 3.8 Flash, thinking medium/temperature 0. Hai POST độc lập theo yêu cầu trước, không retry hoặc gửi bản đầu cho lần sau.
- Request 1 HTTP 200/STOP: sinh 17,94 giây/tổng 19,50 giây gồm Word/render, usage 11594. Word một trang, bảng 4 cột/8 hàng tổng (**7 hàng thân trống**). Không ảnh nhúng/literal escape; hash khớp metadata, đã xem render. Không sửa tay.
- Request 2 HTTP 503 UNAVAILABLE sau 6,31 giây: không Word; Google báo nhu cầu sử dụng cao. Tổng đúng hai request, không thử thêm.
- **Đếm hàng/cột đạt trên bản 1.** Độ giống ảnh toàn bộ chưa đạt: ghi chú gộp với đoạn hướng dẫn, mất dấu đóng ngoặc sau CNTT trong ghi chú, cỡ chữ/khoảng cách/in đậm đề tài khác nguồn; dấu đỏ không dựng. Không coi bản 1 là chính xác hoàn toàn.
- Lần này đồng thời đổi ngôn ngữ prompt và thêm dữ liệu bảng Paddle, nên chưa xác định riêng yếu tố nào giúp đếm đúng. Chưa chứng minh ổn định nhiều lần hoặc trên nhiều tài liệu. Trên mẫu này cấu trúc Paddle là nguồn bổ trợ hữu ích; không xóa Paddle khỏi dự án dựa trên thử nghiệm chỉ ảnh trước.
- Đã quét output không có khóa, xóa bản khóa tạm và dừng container thử. py_compile đạt; sửa lỗi in Unicode ở terminal Windows của helper, dữ liệu UTF-8 đã lưu đúng. Không đổi endpoint/deploy. Nhật ký 73, testcase 062.
