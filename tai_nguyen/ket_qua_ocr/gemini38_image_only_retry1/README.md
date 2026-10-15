# Gemini chỉ ảnh — lượt thử lại thành công

Ngày 12/10/2026, người dùng yêu cầu thử lại sau HTTP 503. Đây là lượt mới, không phải retry tự động của lượt trước.

- Chỉ ảnh gốc/prompt/schema, không chạy Paddle, không gửi OCR/Word/layout cũ; không truyền --paddle-json. Model gemini-3.8-flash, thinking medium/temperature 0/maxOutputTokens 12000.
- Một POST, không retry/tool loop; HTTP 200/STOP. Sinh 26,96 giây, tổng 28,51 giây gồm xuất/render local. Usage API 6073 tokens, trong đó 1283 thinking tokens; không quy đổi chi phí hay kết luận benchmark từ một lần chạy.
- File `ke_hoach_gemini-3_8-flash_mot_lan.docx`: một trang, chữ/bảng editable, không ảnh nhúng hoặc literal newline escape, có số 246. Hash: `3a8149fd07e128d3a08d0120426f37d9744368f4a7ebd4589cb7aab44e8e90d2`.
- Đã xem toàn trang render, đối chiếu ảnh gốc đã đọc ở lượt trước. Có tiêu đề, nội dung đề tài, sinh viên, ngày và hai vùng ký. **Chưa giống ảnh hoàn toàn:** bảng 4 cột nhưng 10 hàng thân thay vì 7; khoảng cách/cỡ chữ khác; chữ ký trái đặt ngang ngày bên phải, khác độ cao nguồn; đề tài thiếu in đậm nguồn; HOÀ/HÒA khác. Dấu đỏ không dựng trong body.
- So với lượt 057 có Paddle: lượt này vẫn dựng được Word mà không cần Paddle, nhưng 10 hàng thân so với 8 ở lượt có Paddle; cả hai đều sai số hàng. Chưa đủ dữ liệu để kết luận bỏ Paddle tương đương trên mọi tài liệu.
- Không sửa tay hoặc gọi thêm model để sửa. Lưu response/layout/config/prompt/metadata/render và kiểm tra cấu trúc. Khóa tạm đã xóa, output được quét, container thử đã dừng. Chưa đổi endpoint/deploy ứng dụng. Testcase 059, nhật ký 69.
