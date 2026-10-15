# Báo cáo đồ án cập nhật

File bàn giao mới nhất: `Do_an_tot_nghiep_cap_nhat_luong_cong_van.docx`, cập nhật ngày 13/10/2026 từ bản `Do_an_tot_nghiep_cap_nhat_OCR_1_2_3.docx`. Các bản trước được giữ nguyên.

## Cập nhật nghiệp vụ ngày 13/10/2026

- Mục 2.1.4.5: tách nhánh công văn đến lưu chỉ đạo/chuyển xử lý và công văn đi ký PDF/chuyển Văn thư phát hành. Lỗi ký không được coi là đã ký; trạng thái Đã ban hành thuộc bước phát hành. Giữ hình thức bảng kịch bản và các kiểu định dạng cũ.
- Mục 2.6.3.6: giải thích bốn giá trị danh mục và sự khác nhau giữa hướng luân chuyển với thể loại Tờ trình/Quyết định. Việc tách trường hướng luân chuyển là hướng hoàn thiện, chưa được ghi là database đã có.
- Mục 2.7.4: cập nhật phạm vi OCR đã tích hợp endpoint/giao diện, phần quản lý Word/phiên bản và nghiệm thu tài liệu thật còn thiếu. Giữ nguyên mục 1.2.3 và số liệu thử nghiệm 2.7.3.
- Thêm mục 2.8: đối chiếu yêu cầu thiết kế với chức năng hiện có, giới hạn workflow đến/đi, kết quả kiểm thử bộ lọc và công việc cần nghiệm thu. Không đánh dấu hoàn thiện cả hai luồng nghiệp vụ.
- `doc_content.txt`, `tables_content.txt` tại `../noi_dung_trich_xuat/` đã được trích xuất lại từ bản bàn giao; `source.json` lưu nguồn và SHA-256.

Kiểm tra cấu trúc: 18 đoạn cũ được sửa nội dung, 12 đoạn mới; các đoạn ngoài phạm vi, định dạng đoạn cũ và mọi thành phần gói DOCX ngoài `word/document.xml` giữ nguyên. Vẫn có 16 bảng, số hàng từng bảng không đổi. Phông chữ, style, media, lề và header/footer cũ được bảo toàn. Verification lưu cùng tên file với đuôi `.verification.json`.

Đã render bằng `render_docx.py` trong image thử nghiệm có LibreOffice: bản cuối 89 trang. Đã xem toàn bộ trang và chỉnh lại phần thêm để tránh một trang chỉ chứa hai dòng. Ảnh QA tại `kiem_tra_luong_cong_van/render_delivery/`. Các chỗ xuống dòng/ngắt trang của bìa, bảng và sơ đồ gốc được giữ theo nguồn; lần này không chỉnh toàn bộ bố cục cũ hoặc rà soát toàn bộ yêu cầu ngoài phạm vi đến/đi.

Khi mở bản mới trong Microsoft Word, chọn **Update Table → Update entire table** để bổ sung mục 2.7/2.8 và cập nhật số trang theo Word. Cached TOC và các trường hiện có được giữ, không gán số trang LibreOffice vào mục lục Word.

Script tái lập: `../../tools/update_thesis_document_workflows.py`. Chạy bằng runtime Python với `--source` là bản OCR 1.2.3 và `--output` là đường dẫn mới; script không cho ghi đè file nguồn. `--overwrite` chỉ phục vụ lặp kiểm tra bố cục trên output đang làm.

## Lịch sử cập nhật OCR ngày 12/10/2026

File bàn giao tại thời điểm đó: `Do_an_tot_nghiep_cap_nhat_OCR_1_2_3.docx`. File Do_an_tot_nghiep_cap_nhat_OCR.docx đang bị ứng dụng khác khóa khi ghi đè nên giữ nguyên và lưu bản mới riêng.

Bổ sung mục 2.7 sau phần thiết kế cơ sở dữ liệu:

- 2.7.1. Cơ sở lựa chọn PaddleOCR/PP-StructureV3 + Gemini 3.8 Flash.
- 2.7.2. Quy trình ảnh + bằng chứng Paddle → JSON → DOCX editable → văn thư kiểm tra/sửa.
- 2.7.3. Kết quả thử nghiệm 062, đúng 4 cột/7 hàng trống, thời gian/usage/lỗi API và hạn chế.
- 2.7.4. Phạm vi hiện tại, phần tích hợp còn lại và nguồn tham khảo chính thức.

Bản gốc không bị ghi đè. Verification lần thêm mục 2.7 xác nhận nội dung cũ còn nguyên, các thành phần khác trong gói DOCX trùng byte; lưu tại verification_them_2_7.json. Sau đó đã sửa riêng mục 1.2.3 thành **PaddleOCR và mô hình ngôn ngữ lớn**, thay ba đoạn giới thiệu và tên mục trong mục lục để thống nhất với 2.7. File Do_an_tot_nghiep_cap_nhat_OCR_1_2_3.verification.json xác nhận các đoạn khác và các thành phần gói DOCX còn nguyên. Bản trước lần sửa này lưu ở Do_an_tot_nghiep_truoc_sua_1_2_3.docx.

Đã render toàn bộ báo cáo bằng LibreOffice: 87 trang, phần thêm ở trang render 85–87. Đã xem tổng quan toàn báo cáo và từng trang thêm ở độ phân giải trang, sửa giãn cách/ngắt trang để không tràn footer hoặc tạo đoạn lẻ. Số trang có thể khác Microsoft Word; phần bìa, bảng và phân trang cũ vẫn cần kiểm tra theo môi trường Word của người dùng. Đây không phải lần rà soát toàn bộ nội dung và định dạng báo cáo gốc.

Tên mục 1.2.3 trong mục lục đã sửa; mục lục tự động cũ chưa có mục 2.7: khi mở Word, chọn **Update Table → Update entire table** để đưa mục 2.7 vào và cập nhật số trang theo Word. Báo cáo ghi rõ phương án đã được chọn/kiểm thử, chưa tích hợp endpoint hoặc trình soạn thảo mới. Render lần sửa 1.2.3 vẫn có 87 trang, đã xem mục lục và trang nội dung sửa; ảnh QA mới tại kiem_tra_1_2_3/render.

Script tái lập: `../../tools/update_thesis_ocr_section.py` (thêm 2.7) và `../../tools/update_thesis_ocr_intro.py` (sửa 1.2.3). Chưa gọi model mới, thay đổi source ứng dụng hoặc deploy trong lần này.
