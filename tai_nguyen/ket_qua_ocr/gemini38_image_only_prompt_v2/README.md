# Prompt v2 — đếm grid trước khi dựng Word

Ngày 12/10/2026, sửa prompt theo yêu cầu cải thiện số hàng, không đưa số hàng đúng của ảnh mẫu vào prompt và không dùng Paddle.

- Phiên bản `image-layout-v2-grid-counting`: xác định biên trên/dưới và separator, kiểm tra line qua nhiều cột để tránh đếm trùng do ảnh nghiêng, loại bóng/đường nền/baseline chữ. Tách header nhiều dòng khỏi hàng thân.
- Đếm các dải hàng trên xuống rồi kiểm tra dưới lên. Với grid đều không merge: H biên ngang tạo H-1 hàng tổng; schema một header nên H-2 hàng thân. Không áp dụng máy móc cho merge/rule không đầy đủ.
- Kiểm tra len(rows)+blank_rows khớp số hàng quan sát; body hoàn toàn trống dùng rows=[] và blank_rows một lần. Chốt số hàng trước khi căn trang, không thêm/bớt hàng vì chiều cao/spacing.
- Chỉ sửa prompt và metadata phiên bản/hash; schema, ảnh, model/config giữ như lượt 059. Một POST Gemini 3.8 Flash nhận HTTP 503 UNAVAILABLE sau 4,87 giây, thông báo nhu cầu sử dụng cao. Không retry hoặc sửa tay.
- **Chưa có layout/Word/render; chưa chứng minh prompt v2 đếm đúng hơn.** Các Word trước vẫn nguyên trạng.
- Lưu prompt/schema/config/response/metadata, quét output không có khóa, xóa khóa tạm, dừng container. py_compile đạt; không đổi endpoint/deploy. Nhật ký 70, testcase 060.
