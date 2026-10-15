# Kiểm thử Qwen 3.5 9B qua tool calling

Ngày: 11/10/2026. Model thực tế trong LM Studio: `qwen/qwen3.5-9b`.

## Kết quả chạy

- Dùng cùng ảnh `../../mau_cong_van/ke-hoach-thuc-te.jpg`, SHA-256 `3ac3abe4aa4c1c45917b27c49694c9718094b40955f42c62a4e073699dc72e52`, và 30 dòng bằng chứng Paddle đã lưu; không chạy lại Paddle.
- Cùng prompt, định nghĩa tools, runner kiểm tra tham số và bộ xuất của lượt Gemma 4. Không sửa nội dung hay tham số model.
- 423,31 giây, 32 native tool calls: một inspect_source, ba render_document, hai clear_draft. Cả 32 thao tác trả ok, không có lỗi tool hoặc renderer.
- Model chưa gọi finish_document khi chạm giới hạn 32 lượt. `metadata.json` ghi `selected_iteration: null`. Runner thoát lỗi có chủ đích để không coi một bản trung gian là bản cuối.
- Bản đã render gần nhất: [iteration-3/document.docx](iteration-3/document.docx). Đây là bản thử nguyên trạng, không phải file cuối được model chọn. Một trang, văn bản/bảng Word sửa được, không nhúng ảnh chụp. Ở lượt 30 model xóa draft lần nữa rồi thêm hai đoạn trong lượt 31–32, chưa render draft mới.
- Lưu Paddle evidence, response API với native tool_calls/IDs/arguments/finish reason, tool_events, metadata và ba bản render trong thư mục này.

## Kiểm tra bản render thứ ba

Nội dung có tiêu đề chính, đợt/năm học, cụm “chữ ký số”, ghi chú, ngày và hai tên ở phần ký. So với bản Gemma 4 đã thử, ít thiếu nội dung hơn trên mẫu này; đây không phải phép đo độ chính xác OCR trên nhiều tài liệu.

Bố cục chưa giống ảnh: gộp toàn bộ tên trường, quốc hiệu, khoa, tiêu ngữ và tiêu đề thành một đoạn dùng tab, làm các vùng đầu trang nối lẫn nhau; phần thân căn giữa; bảng bốn cột đều nhau, tám hàng trống thay vì bảy; chữ ký thành hai dòng ba cột thay vì hai khối ký song song; số trang 246 nằm cuối tài liệu thay vì góc trên phải. Model tự dựng lại nhưng layout không cải thiện rõ và tiếp tục lặp lại việc xóa draft.

Đã xem ảnh render một trang gần nhất và kiểm tra cấu trúc DOCX. Giữ nguyên mọi lỗi theo yêu cầu, chưa tích hợp production. Kết luận cho lượt thử này: đọc được nhiều nội dung nhưng chưa đạt yêu cầu tái tạo bố cục và chưa tự hoàn tất trong giới hạn đã đặt. Không suy rộng thành kết luận về mọi tác vụ của model.
