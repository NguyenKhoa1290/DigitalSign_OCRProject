# Tài nguyên dự án

- `tai_lieu_goc/`: bản đồ án Word gốc.
- `noi_dung_trich_xuat/`: văn bản và nội dung bảng trích xuất từ tài liệu.
- `mau_cong_van/`: ảnh công văn thực tế dùng thử nghiệm OCR.
- `ket_qua_ocr/`: bằng chứng Paddle + Gemini được báo cáo tham chiếu; không phải dữ liệu ứng dụng nạp lúc chạy.
- `ket_qua_kiem_thu/`: ảnh chụp giao diện từ các script kiểm thử; ảnh được Git bỏ qua như trước.
- `kiem_tra_bao_cao/`: xác minh cấu trúc và bản render cuối của báo cáo.
- `bao_cao/`: bản nguồn ngày 13/10 dùng tái lập; báo cáo bàn giao mới nhất nằm trong [tai_lieu/05_bao_cao](../tai_lieu/05_bao_cao/README.md).
- `../backup/20261019/`: [các phương án cũ](../backup/20261019/README.md), runner thử nghiệm, template tham khảo, báo cáo cũ và đầu ra trung gian đã được chuyển khỏi cây làm việc chính.

Script xử lý nằm trong `../tools`, script kiểm thử nằm trong `../tests`. Các đường dẫn tài nguyên được tính từ vị trí script, không phụ thuộc thư mục chạy lệnh.

Trích xuất bảng từ báo cáo hiện tại: `python tools/extract_tables.py --document tai_lieu/05_bao_cao/Do_an_tot_nghiep_cap_nhat_phan_quyen_xoa.docx` (cần Python và thư viện `python-docx`). Khi không truyền `--document`, script vẫn đọc bản gốc ở `tai_lieu_goc`, không phải báo cáo mới nhất.

Phương án sản phẩm hiện tại chạy trong `OCRService/app`: file tải lên → PaddleOCR → Gemini → Word. Các runner LM Studio/tool calling chỉ giữ để xem lại thử nghiệm trong kho lưu trữ; không dùng làm hướng dẫn vận hành hiện tại.

Tài liệu vận hành và nhật ký vẫn nằm tại `../tai_lieu` và `../tiendo`. Tài nguyên frontend phục vụ ứng dụng vẫn nằm trong `../Frontend/wwwroot`.
