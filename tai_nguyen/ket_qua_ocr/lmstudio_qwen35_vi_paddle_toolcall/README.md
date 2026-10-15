# LM Studio Qwen 3.5 9B — thử xuất Word bằng tool call

Ngày 13/10/2026. Dùng model `qwen/qwen3.5-9b` đang nạp trong LM Studio; gửi ảnh gốc và bằng chứng Paddle/PP-Structure đã lưu từ testcase 062, cùng prompt tiếng Việt. Không chạy Paddle inference mới, không gọi Google và không sửa nội dung model trả về.

Model gọi công cụ 4 lần trong 4 lượt, tổng 237,29 giây: hai lần `render_word` đầu bị từ chối vì thiếu tiêu đề bảng; lần thứ ba tạo được Word; lần thứ tư gọi `finish_document` chọn iteration 3. DOCX hợp lệ về cấu trúc tệp và trùng hash với iteration được chọn.

**Kết quả nội dung/bố cục: chưa đạt.** Paddle có 8 hàng HTML tổng: 1 hàng tiêu đề và 7 hàng thân trống. Layout model chọn có 9 hàng rỗng trong `rows` và `blank_rows=8`, thành 17 hàng thân trống. Bản render tràn thành 2 trang; trang đầu chứa gần hết bảng, trang hai chỉ còn phần chữ ký. Các vùng quốc hiệu/tiêu đề và thông tin ký không giữ được bố cục song song như ảnh; số trang 246 và dấu đỏ cũng không xuất hiện trong render. Không chỉnh số hàng hay bố cục bằng tay.

Đã lưu prompt, Paddle input, các tool calls/responses, ba layout trung gian, DOCX và render để đối chiếu. Đây xác nhận model thực hiện được tool call và tự xem render trước khi chọn file, nhưng phép thử này không đạt định dạng mẫu. Phương án Paddle + Gemini đã chốt trước đó không thay đổi; endpoint ứng dụng chưa tích hợp.
