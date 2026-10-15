# Thử nghiệm PaddleOCR và LLM xuất Word

Thực hiện ngày 11/10/2026 trên ảnh `../../mau_cong_van/ke-hoach-thuc-te.jpg`.

PaddleOCR 2.9.1 nhận diện thật 30 dòng trên CPU trong container OCR hiện có. Các dòng có nội dung, confidence và bounding box được gửi cùng ảnh cho model `gemma4-12b-qat-uncensored-hauhaucs-balanced` chạy tại LM Studio local. Lượt đầu không streaming hết thời gian chờ 240 giây; lượt streaming hoàn tất trong 307,99 giây (tính cả nhập thư viện và xuất Word của script, không gồm lượt chạy Paddle ban đầu).

## Kết quả

- `paddle_lines.json`: bằng chứng nhận diện từ Paddle.
- `llm_response.json`, `layout.json`: kết quả bố cục LLM nguyên trạng; không dùng kết quả JSON cũ để thay thế lượt nhận diện này.
- `ke_hoach_paddle_llm.docx`: bản Word tự động, còn lỗi đọc và ước lượng bố cục.
- `layout_reviewed.json`, `review_changes.json`: dữ liệu đã đối chiếu với ảnh và danh sách hiệu chỉnh thủ công sau OCR.
- `ke_hoach_paddle_llm_da_doi_chieu.docx`: bản Word đã đối chiếu, chỉnh sửa được bằng Word; một trang A4, bảng 4 cột và 7 hàng thân bảng trống.
- `render_raw/`, `render_reviewed/`: PDF và PNG phục vụ kiểm tra thị giác bằng `render_docx.py`, LibreOffice và font Times New Roman của máy.
- `metadata.json`: model, SHA-256 ảnh, số dòng Paddle và thời gian thực hiện.

Bản tự động đọc sai tên trường, tiêu đề, cụm “chữ ký số” và một phần ghi chú; đếm 8 hàng trống thay vì 7, ước lượng cột thời gian quá rộng và cột kết quả quá hẹp. Những lỗi này được sửa bằng đối chiếu trực tiếp ảnh, không phải do model tự sửa. Dấu đỏ chồng lên tiêu đề không được tái tạo. Bản đã đối chiếu không được coi là bằng chứng rằng OCR tự động đạt độ chính xác tuyệt đối.

## Mã thử nghiệm

`tools/test_paddle_llm_word.py` gọi Paddle + LLM và lưu bằng chứng. `OCRService/app/services/layout_exporter.py` xuất các khối paragraph, columns và table theo JSON; không chứa nội dung cố định của biểu mẫu này. Bộ xuất giữ ô trống, dùng bảng Word thật và sửa lỗi gộp bảng liên tiếp làm mất đường viền khi render.

Đây là thử nghiệm local độc lập; endpoint xuất Word hiện tại chưa được chuyển sang bộ xuất bố cục mới.

Chạy lại trong container OCR có module mới:

```powershell
docker exec hau_ocr_service mkdir -p /tmp/hau-ocr-trial /app/tools
docker cp tai_nguyen/mau_cong_van/ke-hoach-thuc-te.jpg hau_ocr_service:/tmp/hau-ocr-trial/input.jpg
docker cp OCRService/app/services/layout_exporter.py hau_ocr_service:/app/app/services/layout_exporter.py
docker cp tools/test_paddle_llm_word.py hau_ocr_service:/app/tools/test_paddle_llm_word.py
docker exec hau_ocr_service python /app/tools/test_paddle_llm_word.py --image /tmp/hau-ocr-trial/input.jpg --output-dir /tmp/hau-ocr-trial/rerun --model gemma4-12b-qat-uncensored-hauhaucs-balanced
docker cp hau_ocr_service:/tmp/hau-ocr-trial/rerun/. tai_nguyen/ket_qua_ocr/paddle_llm_thu_nghiem/rerun/
```

LM Studio cần phục vụ API tại cổng 1234 cho container truy cập. Để dùng bằng chứng Paddle đã lưu thay vì chạy lại, script nhận thêm `--paddle-json` trỏ tới JSON trong container.
