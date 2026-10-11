# Triển khai thử trên máy Windows hiện tại

Workflow `.github/workflows/ci-cd.yml` vẫn chạy CI và đẩy image theo SHA lên GHCR khi push `main`. Tùy chọn `deploy_to_local` triển khai đúng image đó lên Docker Desktop trên máy `E:\DigitalSign_OCRProject`; không dùng SSH hay các secret `VPS_*`.

1. Trong repository GitHub, vào **Settings > Actions > Runners > New self-hosted runner** và làm theo hướng dẫn dành cho Windows x64. Khi cấu hình runner, thêm nhãn `hau-local`. Xem [hướng dẫn chính thức của GitHub](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners). Runner cần chạy dưới tài khoản có quyền dùng Docker Desktop. Máy và Docker Desktop phải đang hoạt động khi triển khai.
2. Giữ `.env` và `.env.tunnel` trong `E:\DigitalSign_OCRProject`. Hai file chứa secret và không được commit. Kiểm tra Docker đang chạy bằng `docker compose ps`.
3. Sau khi CI của commit trên `main` thành công, vào **Actions > CI/CD > Run workflow**, chọn nhánh `main` và bật `deploy_to_local`. Workflow sẽ kéo image theo SHA, cập nhật Compose, tạo lại `cloudflared` và kiểm tra `http://127.0.0.1:5227/health`.

Nếu runner chưa đăng ký hoặc đang tắt, job `deploy-local` sẽ chờ runner. Tùy chọn `deploy_to_vps` hiện có vẫn dành cho VPS Linux qua SSH. Không bật cả hai tùy chọn trong cùng lần chạy trừ khi thực sự muốn triển khai lên cả hai nơi.
