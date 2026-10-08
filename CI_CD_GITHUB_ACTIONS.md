# CI/CD Docker với GitHub Actions

Workflow nằm tại `.github/workflows/ci-cd.yml` và có ba giai đoạn:

1. Pull request và push vào `main`: restore, chạy bốn bộ test .NET và kiểm tra Docker Compose.
2. Sau khi kiểm tra thành công: build sáu Docker image. Với push vào `main`, image được đẩy lên GitHub Container Registry (GHCR) với hai tag: `sha-<commit>` và `latest`.
3. Chỉ với push vào `main`: GitHub Actions SSH vào VPS, pull đúng tag `sha-<commit>`, chạy Docker Compose với `--no-build` và chờ health endpoint của frontend.

VPS không build source nên phù hợp hơn với cấu hình RAM thấp. Pipeline không tự đưa secrets ứng dụng vào GitHub: `.env` và `.env.tunnel` chỉ tồn tại trên VPS.

## 1. Chuẩn bị package GHCR

Lần push đầu tiên vào `main` tạo sáu package dưới tiền tố:

```text
ghcr.io/nguyenkhoa1290/digitalsign_ocrproject/<service>
```

Trong từng package, kiểm tra **Package settings > Manage Actions access** để repository này có quyền ghi package. Nếu package private, tạo personal access token **classic** của tài khoản có quyền đọc package và cấp scope **`read:packages`**. Lưu token đó làm environment secret `GHCR_TOKEN`.

## 2. Chuẩn bị VPS một lần

Trên VPS cần Docker Engine, Docker Compose v2, Git, Curl và một bản clone sạch của repository. Ví dụ thư mục deploy là `/opt/digitalsign`:

```bash
sudo mkdir -p /opt/digitalsign
sudo chown "$USER":"$USER" /opt/digitalsign
git clone git@github.com:NguyenKhoa1290/DigitalSign_OCRProject.git /opt/digitalsign
cd /opt/digitalsign
cp .env.example .env
nano .env
nano .env.tunnel
```

Điền toàn bộ secret production vào `.env` và `TUNNEL_TOKEN` vào `.env.tunnel`. Hai file này bị Git bỏ qua, vì vậy `git checkout` của pipeline không ghi đè chúng.

VPS phải có một deploy key chỉ đọc để tự `git fetch` repository private. Thêm public key đó vào GitHub tại **Repository settings > Deploy keys**, bật **Allow write access** là không cần thiết.

Chạy tay lần đầu để tạo volume dữ liệu và kiểm tra tunnel:

```bash
cd /opt/digitalsign
docker compose -f docker-compose.yml -f docker-compose.tunnel.yml up -d --build
```

Sau lần đầu, pipeline chỉ pull image và không build trên VPS. Không chạy `docker compose down -v` vì lệnh đó xóa volume PostgreSQL, MinIO, Kafka và chứng thư số.

## 3. GitHub Environment và secrets

Tạo environment tên `production` trong **Settings > Environments**. Có thể bật required reviewer để mỗi lần deploy phải được duyệt. Với repository private trên GitHub Free, environment secrets không khả dụng; khi đó dùng repository secrets hoặc nâng lên GitHub Pro/Team.

Thêm các environment secrets sau:

| Secret | Giá trị |
|---|---|
| `VPS_HOST` | IP hoặc hostname SSH của VPS |
| `VPS_PORT` | Cổng SSH, thường là `22` |
| `VPS_USER` | User Linux chạy Docker |
| `VPS_SSH_KEY` | Private key để GitHub Actions SSH vào VPS |
| `VPS_HOST_FINGERPRINT` | SHA256 fingerprint của SSH host, lấy bằng `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub -E sha256` trên VPS |
| `VPS_DEPLOY_PATH` | Đường dẫn clone repository, ví dụ `/opt/digitalsign` |
| `GHCR_TOKEN` | Personal access token classic có scope `read:packages` |

`VPS_SSH_KEY` là key GitHub Actions dùng để vào VPS. Nó khác deploy key mà VPS dùng để đọc repository.

## 4. Luồng chạy và rollback

Khi pull request mở vào `main`, chỉ CI chạy; không push image và không SSH. Khi merge/push vào `main`, CI xong mới build/push image và deploy.

Mỗi bản deploy dùng tag theo commit, ví dụ `sha-abc123...`, không dùng riêng `latest`. Nếu cần rollback, chạy trên VPS với tag cũ:

```bash
cd /opt/digitalsign
export IMAGE_PREFIX=ghcr.io/nguyenkhoa1290/digitalsign_ocrproject
export IMAGE_TAG=sha-<commit-cu>
docker compose -f docker-compose.yml -f docker-compose.tunnel.yml pull
docker compose -f docker-compose.yml -f docker-compose.tunnel.yml up -d --no-build
```

Kiểm tra sau deploy:

```bash
curl --fail http://127.0.0.1:5227/health
docker compose -f docker-compose.yml -f docker-compose.tunnel.yml ps
```

Lưu ý: stack hiện có Kafka và OCR. VPS 1 GB RAM không phù hợp để chạy toàn bộ stack này; cần tách OCR/Kafka hoặc tăng tài nguyên trước khi dùng pipeline làm môi trường vận hành lâu dài.
