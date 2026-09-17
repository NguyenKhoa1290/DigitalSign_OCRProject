# Quy Trình Làm Việc Chuẩn

Từ các bước phát triển tiếp theo, quy trình làm việc của dự án sẽ đi theo thứ tự sau:

```text
1. Viết/Sửa code
2. Build code
3. Build/triển khai Docker
4. Test theo test case
5. Ghi lại test case đã chạy
6. Báo cáo kết quả
```

## 1. Viết/Sửa code

- Xác định đúng phạm vi cần sửa.
- Đọc lại code liên quan trước khi thay đổi.
- Không sửa lan man ngoài yêu cầu.
- Nếu thay đổi logic nghiệp vụ, cập nhật tài liệu song song.

## 2. Build code

Sau khi sửa code, cần build các project liên quan.

Ví dụ:

```powershell
dotnet build HAU_DigitalSign_OCR.slnx
```

Với OCRService, kiểm tra dependencies Python khi có thay đổi:

```powershell
cd OCRService
python -m compileall app
```

## 3. Build/triển khai Docker

Sau khi build code ổn, build Docker image liên quan:

```powershell
docker compose build <service-name>
```

Nếu thay đổi nhiều service hoặc hạ tầng:

```powershell
docker compose build
docker compose up -d
```

Kiểm tra container:

```powershell
docker compose ps
```

## 4. Test theo test case

Mỗi thay đổi cần có test case tương ứng, tối thiểu gồm:

- Test build.
- Test container chạy được.
- Test API/flow chính bị ảnh hưởng.
- Test lỗi/edge case nếu thay đổi logic nghiệp vụ.

Ví dụ health check:

```powershell
Invoke-WebRequest http://localhost:5000/health -UseBasicParsing
Invoke-WebRequest http://localhost:5048/health -UseBasicParsing
Invoke-WebRequest http://localhost:5051/api/ocr/health -UseBasicParsing
```

## 5. Ghi lại test case đã chạy

Sau khi test, ghi lại:

| Trường | Nội dung |
|---|---|
| Mã test | Ví dụ `TC-SIGN-001` |
| Mục tiêu | Test chức năng gì |
| Dữ liệu test | User, role, document, file |
| Các bước chạy | Các thao tác/lệnh/API |
| Kết quả mong đợi | Expected result |
| Kết quả thực tế | Actual result |
| Trạng thái | Pass/Fail/Blocked |
| Ghi chú | Log, lỗi, hướng xử lý |

Nếu test liên quan một tính năng lớn, thêm vào file nhật ký hoặc tạo file test riêng trong `tiendo/`.

## 6. Báo cáo kết quả

Khi bàn giao sau mỗi lần sửa, báo cáo ngắn gọn:

- Đã sửa những gì.
- Đã build bằng lệnh nào.
- Đã chạy Docker thế nào.
- Đã test những test case nào.
- Test pass/fail ra sao.
- Còn rủi ro hoặc việc cần làm tiếp không.

## Nguyên tắc cập nhật tài liệu song song

Khi code thay đổi, cần cập nhật tài liệu tương ứng:

- Kiến trúc/luồng nghiệp vụ: `tiendo/1_kien_truc.md`
- Việc đã làm/TODO: `tiendo/2_da_lam.md`
- Nhật ký làm việc: `tiendo/3_nhat_ky.md`
- Cấu trúc code: `tiendo/4_cau_truc_code.md`
- Hạ tầng/DB/Docker: `tiendo/5_co_so_ha_tang_db.md`
- Quy trình làm việc/test: `tiendo/6_quy_trinh_lam_viec.md`
- Test case đã chạy: `tiendo/7_test_cases.md`
- Hướng dẫn deploy: `TRIEN_KHAI_DOCKER.md`
