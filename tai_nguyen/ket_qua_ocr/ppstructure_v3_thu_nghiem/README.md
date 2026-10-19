# PP-StructureV3 không dùng LLM — 12/10/2026

Ảnh: `../../mau_cong_van/ke-hoach-thuc-te.jpg`, SHA-256 `3ac3abe4aa4c1c45917b27c49694c9718094b40955f42c62a4e073699dc72e52`.

## Môi trường

Container riêng `hau_ppstructure_trial`, virtualenv `/opt/ppstructure`: PaddleOCR 3.7.0, PaddlePaddle 3.3.1, PaddleX 3.7.2. CPU 4 thread, tắt MKLDNN. Bật orientation, UVDoc, textline orientation, layout và table recognition; tắt formula/chart/seal/region detection. Không gọi LLM/cloud, không sửa tay hoặc hardcode số hàng/cột. Không đổi dependency hoặc redeploy service OCR chính 2.9.1/2.6.2.

Xuất trực tiếp `result.save_to_word()` của PaddleX; mỗi lượt có JSON, pipeline.yaml, Markdown, HTML bảng, ảnh nhận diện, DOCX, metadata và run.log. Render qua LibreOffice/render_docx.py, đã xem cả ba trang PNG.

## Ba lượt thử

- `native/`: OCRv3, 32 dòng/15 vùng. Khởi tạo 57,87 giây; xử lý/xuất 15,48 giây; tổng 73,36 giây.
- `ocrv5/`: OCRv5 server detector/latin mobile recognizer, 32 dòng/15 vùng. Khởi tạo 16,39 giây; xử lý/xuất 35,12 giây; tổng 51,53 giây.
- `ocrv6/`: override bằng PP-OCRv6_medium_det/rec, 33 dòng/15 vùng. Khởi tạo 19,10 giây; xử lý/xuất 31,67 giây; tổng 50,78 giây. ocr_version=PP-OCRv5 là cấu hình nền, tên model thực tế trong metadata và pipeline.yaml là OCRv6.

Thời gian có tải model/font khi thiếu cache, không phải benchmark nhiều lần. Cả ba Word một trang, bảng thật 4 cột và 7 hàng thân trống, không nhúng ảnh chụp. `verification.json` lưu kiểm tra cấu trúc.

OCRv3 mất nhiều dấu/chữ và bỏ sót một phần đề tài. OCRv5 nhận nhiều nội dung hơn nhưng vẫn mất nguyên âm/dấu. OCRv6 cải thiện một số chữ, vẫn nhầm ký tự/dấu và thiếu chữ. Chưa đo CER/WER trên bộ có bản chuẩn.

Bộ xuất Word gốc gộp đầu trang, nhận chữ dấu đỏ vào tiêu đề, không giữ hai khối ký song song, bảng cột đều/hàng thấp và chuyển số trang xuống footer. **Chưa đạt chất lượng chữ và bố cục giống ảnh.** [Word thử OCRv6](ocrv6/page-1/ppstructure-input.docx) giữ nguyên kết quả; hai bản trước được lưu để đối chiếu.

## Chạy lại

Script `backup/20261019/tools_ocr/test_ppstructure_word.py`, dependencies đầy đủ tại `requirements.freeze.txt`. Môi trường riêng cài `paddlepaddle==3.3.1` và `paddleocr[doc-parser,doc2md]==3.7.0`.

```bash
/opt/ppstructure/bin/python test_ppstructure_word.py --image input.jpg --output-dir output-moi --ocr-version PP-OCRv5
```

OCRv6 thêm `--detector PP-OCRv6_medium_det --recognizer PP-OCRv6_medium_rec`. Output phải chưa tồn tại. Có `--no-unwarp`, nhưng biến thể đó chưa chạy trong lần này. Renderer cần LibreOffice/Poppler, không cần LM Studio.

Đã xóa `hau-ocr-render` theo yêu cầu. Tận dụng thư viện OS/LibreOffice cũ làm base cho container Paddle riêng; không xóa image OCR của stack. Container mới dừng sau thử nghiệm, giữ môi trường/cache để chạy lại.

Hướng tiếp theo: cải thiện ảnh/nhận diện tiếng Việt và dựng DOCX từ tọa độ vùng/cell. LLM/cloud chỉ là phương án bổ sung; chưa tích hợp pipeline thử nghiệm vào ứng dụng.

Nguồn API: [PP-StructureV3 chính thức](https://github.com/PaddlePaddle/PaddleOCR/blob/main/docs/version3.x/pipeline_usage/PP-StructureV3.en.md). Hỗ trợ OCRv5/OCRv6 tiếng Việt đã đối chiếu mã `_get_ocr_model_names` và `_PPOCRV6_LANGS` trong package 3.7.0, vì bảng ngôn ngữ trên web chưa hoàn toàn đồng bộ với package.
