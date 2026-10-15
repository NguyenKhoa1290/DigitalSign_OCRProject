# LM Studio Qwen 3.5 9B với prompt tiếng Việt và bảng Paddle

Thực thi theo yêu cầu dùng model đang nạp; hoàn tất kiểm tra ngày 13/10/2026. API trạng thái LM Studio xác nhận `qwen/qwen3.5-9b`, VLM, Q4_K_M, state=loaded, context nạp 30208; không chọn model chỉ dựa trên danh sách model đã tải.

- Cùng ảnh và evidence native PP-Structure đã dùng ở testcase 062: 32 dòng chữ, HTML bảng và cell boxes. Paddle có 8 hàng tổng = 1 header + 7 hàng thân trống. Không chạy Paddle inference mới hoặc sửa evidence.
- Cùng prompt tiếng Việt `image-layout-v3-vi-grid-counting`, hash b966e8411a6da37ef824c5b1ac932c71e29ae27a92aa831199ab8f2b805efb46. JSON schema qua `/v1/chat/completions`, temperature 0, max_tokens 12000, hai POST độc lập local; không tool loop/retry hoặc Google API. Không cấu hình thinking medium của Google cho Qwen; LM Studio dùng thiết lập reasoning hiện hữu.
- Lượt 1 HTTP 200/stop, 346,56 giây; lượt 2 HTTP 200/stop, 380,88 giây. Mỗi response có prompt_tokens=8135, completion_tokens=5217, total_tokens=13352; reasoning_tokens=3567 theo API. Chưa so benchmark thời gian trực tiếp với Google.
- Hai layout trùng SHA-256 `11870404b6ed0e93cc04033bfdb7faa55be55e9ecef0d56277183550d038898b`. Bảng có 4 headers/4 widths, nhưng rows chứa **10 mảng rỗng**, blank_rows=0; không đủ 4 ô mỗi hàng và sai số hàng so nguồn.
- Bộ kiểm tra từ chối với `Table cell/width count mismatch`; **không có DOCX hoặc render**. Không tự thêm ô trống/chuyển đổi rows hoặc sửa số hàng để tạo file hợp lệ. Đầu trang và metadata trong JSON còn xếp thành đoạn dọc thay vì các vùng song song; nhãn thời gian thiếu dấu “tuần”. Chưa đánh giá định dạng Word vì không tạo được Word.
- Kết luận riêng phép thử này: Qwen 3.5 9B chưa đạt tái tạo bố cục với prompt/evidence hiện tại. Không kết luận mọi model local đều không làm được; không đổi phương án Paddle + Gemini đã được chọn.
- Giữ raw response, prompt/config/schema, evidence, layout, metadata và verification theo request. Container thử hiện đã dừng; local trial không dùng khóa Google. Không đổi endpoint/deploy hoặc nội dung báo cáo đã bàn giao. Nhật ký 76, testcase 063.

Tham chiếu cách gọi schema: [LM Studio Structured Output](https://lmstudio.ai/docs/developer/openai-compat/structured-output).
