# Tài nguyên dự án

- `tai_lieu_goc/`: bản đồ án Word gốc.
- `noi_dung_trich_xuat/`: văn bản và nội dung bảng trích xuất từ tài liệu.
- `mau_cong_van/`: ảnh công văn thực tế dùng thử nghiệm OCR.
- `ket_qua_ocr/`: kết quả nhận diện từ LM Studio.
- `ket_qua_kiem_thu/`: ảnh chụp giao diện từ các script kiểm thử; ảnh được Git bỏ qua như trước.

Script xử lý nằm trong `../tools`, script kiểm thử nằm trong `../tests`. Các đường dẫn tài nguyên được tính từ vị trí script, không phụ thuộc thư mục chạy lệnh.

Trích xuất bảng: `python tools/extract_tables.py` (cần Python và thư viện `python-docx`). Có thể chỉ định tài liệu và đầu ra khác bằng `--document` và `--output`.

Thử nhận diện ảnh bằng LM Studio local: `powershell -File tools/test_lmstudio_form.ps1`. Script đọc `mau_cong_van/ke-hoach-thuc-te.jpg` và ghi vào `ket_qua_ocr/lmstudio_form_result.json`.

Tài liệu vận hành và nhật ký vẫn nằm tại `../tai_lieu` và `../tiendo`. Tài nguyên frontend phục vụ ứng dụng vẫn nằm trong `../Frontend/wwwroot`.
