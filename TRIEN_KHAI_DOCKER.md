# Triển khai HAU DigitalSign OCR bằng Docker trên máy khác

Tài liệu này dùng cho trường hợp mang source code sang một máy mới và chạy toàn bộ hệ thống bằng Docker Compose.

Nếu vừa chuyển Docker Desktop từ Hyper-V sang WSL2 hoặc Docker bị mất hết image/container, xem thêm file hướng dẫn nhanh: `HUONG_DAN_KHOI_PHUC_DOCKER_WSL2.md`.

## 1. Yêu cầu trên máy mới

Cài sẵn:

- Docker Desktop hoặc Docker Engine.
- Docker Compose v2.
- Git nếu lấy source bằng `git clone`.
- Kết nối internet trong lần chạy đầu để pull image nền và package.

Kiểm tra nhanh:

```powershell
docker --version
docker compose version
```

Máy nên có tối thiểu 8 GB RAM. OCRService dùng PaddleOCR nên lần build đầu khá lâu và image lớn.

Nếu chỉ triển khai bằng Docker Compose thì không bắt buộc cài .NET SDK trên máy deploy. `Frontend/Dockerfile` đã tự cài `python3` và `wasm-tools` trong build stage để publish Blazor WebAssembly tối ưu.

Nếu muốn build/publish frontend trực tiếp ngoài Docker, cài thêm:

```powershell
dotnet workload install wasm-tools
```

Lưu ý: file `docker-compose.yml` hiện đặt `container_name` cố định dạng `hau_*`. Vì vậy không nên chạy song song 2 bản project trên cùng một máy, trừ khi bạn đổi port và đổi `container_name`/project name trong Compose.

## 2. Các port cần trống

Trước khi chạy, đảm bảo các port sau chưa bị app khác chiếm:

| Thành phần | URL/port trên máy host |
|---|---|
| Frontend | `http://localhost:5227` |
| API Gateway | `http://localhost:5000` |
| IdentityService | `http://localhost:5048` |
| DocumentService | `http://localhost:5049` |
| SignService | `http://localhost:5050` |
| OCRService | `http://localhost:5051` |
| PostgreSQL | `localhost:5432` |
| MinIO API | `localhost:9000` |
| MinIO Console | `http://localhost:9001` |
| Kafka | `localhost:9092` |

Nếu port bị trùng, sửa phần `ports:` trong `docker-compose.yml`.

## 2.1. Dữ liệu Docker cần lưu ý

Compose hiện tạo các volume chính:

| Volume | Dữ liệu |
|---|---|
| `hau_postgres_data` | Database PostgreSQL |
| `hau_minio_data` | File upload trong MinIO |
| `hau_kafka_data` | Dữ liệu Kafka local |
| `hau_sign_certs` | Root CA và PFX user của SignService |

Nếu mang môi trường thật sang máy khác, cần backup tối thiểu PostgreSQL, MinIO và `hau_sign_certs`. Kafka thường có thể dựng lại topic nếu chỉ cần khôi phục trạng thái nghiệp vụ từ DB/file.

## 3. Chuyển source sang máy mới

Cách 1: clone từ Git:

```powershell
git clone <repo-url> DigitalSign_OCRProject
cd DigitalSign_OCRProject
```

Cách 2: copy thư mục project:

- Copy toàn bộ source sang máy mới.
- Nên bỏ qua các thư mục build tạm như `bin/`, `obj/`, `.vs/` nếu chỉ cần source.
- Mở terminal tại thư mục root, nơi có file `docker-compose.yml`.

## 4. Tạo file `.env` cho Docker Compose

Docker Compose có thể chạy ngay bằng các default dev trong `docker-compose.yml`, nhưng khi mang sang máy khác hoặc triển khai thật nên tạo file `.env` riêng:

```powershell
Copy-Item .env.example .env
```

Sau đó mở `.env` và đổi tối thiểu:

- `POSTGRES_PASSWORD`
- `MINIO_ROOT_PASSWORD`
- `JWT_SECRET_KEY`
- `OCR_SERVICE_TOKEN`

Lưu ý:

- `JWT_SECRET_KEY` phải giống nhau cho IdentityService, ApiGateway, DocumentService và SignService.
- `OCR_SERVICE_TOKEN` phải giống nhau giữa DocumentService và OCRService.
- Không commit file `.env` thật vào Git. Repo đã có `.gitignore` và `.dockerignore` để bỏ qua `.env`.

Nếu chỉ chạy local/dev nhanh mà chưa tạo `.env`, hệ thống vẫn dùng default dev như trước.

## 5. Lưu ý nếu truy cập Frontend từ máy khác trong LAN

Frontend hiện đang build với API base:

```text
http://localhost:5000/
```

Nếu mở trình duyệt ngay trên máy deploy thì giữ nguyên.

Nếu mở từ một máy khác, ví dụ máy deploy có IP `192.168.1.50`, cần sửa `Frontend/Program.cs` trước khi build:

```csharp
BaseAddress = new Uri("http://192.168.1.50:5000/")
```

Sau đó build lại frontend:

```powershell
docker compose build frontend
docker compose up -d frontend
```

## 6. Build image lần đầu

Tại thư mục root project:

```powershell
docker compose build
```

Lần đầu có thể lâu, đặc biệt ở OCRService vì phải cài PaddleOCR/PaddlePaddle.

PaddleOCR có thể tải model ở lần OCR đầu tiên. Nếu triển khai ở máy không có internet, nên chạy thử OCR một lần trên máy có internet trước, hoặc chuẩn bị riêng phần cache/model PaddleOCR.

Frontend build image cũng cần internet ở lần đầu để cài .NET workload `wasm-tools`. Nếu thiếu Python trong build image, WebAssembly publish có thể lỗi `unable to find python in $PATH`; Dockerfile hiện đã xử lý bằng `python3`.

## 7. Chạy toàn bộ hệ thống

```powershell
docker compose up -d
```

Kiểm tra trạng thái:

```powershell
docker compose ps
```

Kết quả mong muốn:

- `hau_postgres` là `healthy`.
- `hau_identity_service` là `healthy`.
- Các container còn lại ở trạng thái `Up`.
- `hau_minio_init` có thể `Exited (0)`, đây là bình thường vì container này chỉ tạo bucket `documents` rồi thoát.

## 8. Kiểm tra sau khi chạy

Kiểm tra endpoint:

```powershell
Invoke-WebRequest http://localhost:5000/health -UseBasicParsing
Invoke-WebRequest http://localhost:5000/ -UseBasicParsing
Invoke-WebRequest http://localhost:5048/health -UseBasicParsing
Invoke-WebRequest http://localhost:5051/api/ocr/health -UseBasicParsing
Invoke-WebRequest http://localhost:5227/ -UseBasicParsing
```

Kết quả mong muốn:

- Gateway health trả `Healthy`.
- Identity health trả `Healthy`.
- OCR health trả JSON có `status: ok`.
- Frontend trả HTML.

Kiểm tra login qua Gateway:

```powershell
$payload = @{ username = "admin"; password = "Admin@123" } | ConvertTo-Json
Invoke-RestMethod `
  -Uri "http://localhost:5000/api/auth/login" `
  -Method Post `
  -Body $payload `
  -ContentType "application/json"
```

Tài khoản seed:

| Username | Password | Role |
|---|---|---|
| `admin` | `Admin@123` | `Admin` |

## 9. Kiểm tra MinIO và Kafka

MinIO Console:

```text
http://localhost:9001
```

Đăng nhập:

```text
Username: minioadmin
Password: minioadmin
```

Nếu đã đổi `.env`, dùng `MINIO_ROOT_USER` và `MINIO_ROOT_PASSWORD` trong file `.env`.

Bucket mặc định cần có:

```text
documents
```

Kiểm tra topic Kafka:

```powershell
docker compose exec -T kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list
```

Kết quả mong muốn có:

```text
document.uploaded
```

Lưu ý hiện tại: Kafka upload event vẫn gửi `token` rỗng, nhưng Docker compose đã cấu hình `SERVICE_TOKEN` cho OCRService và `ServiceAuth__OcrServiceToken` cho DocumentService. Khi OCRService không nhận JWT từ event, service sẽ dùng header nội bộ `X-Service-Token` để PATCH kết quả OCR về DocumentService.

## 10. Xem log khi cần debug

Xem toàn bộ log:

```powershell
docker compose logs -f
```

Xem log từng service:

```powershell
docker compose logs -f identity-service
docker compose logs -f document-service
docker compose logs -f sign-service
docker compose logs -f ocr-service
docker compose logs -f api-gateway
```

Một số log EF Core báo lỗi khi kiểm tra bảng `__EFMigrationsHistory` trong lần chạy DB rỗng đầu tiên có thể xuất hiện, nhưng nếu sau đó có dòng migration hoàn tất và container vẫn `Up` thì không phải lỗi chặn chạy.

## 11. Dừng, chạy lại, reset dữ liệu

Dừng container nhưng giữ dữ liệu:

```powershell
docker compose down
```

Chạy lại:

```powershell
docker compose up -d
```

Xóa container và xóa toàn bộ dữ liệu PostgreSQL/MinIO/Kafka/certs:

```powershell
docker compose down -v
```

Chỉ dùng `down -v` khi chắc chắn muốn reset dữ liệu.

## 12. Mang cả dữ liệu sang máy khác

Nếu chỉ cần chạy môi trường mới rỗng, bỏ qua phần này.

### 12.1. Backup PostgreSQL trên máy cũ

```powershell
New-Item -ItemType Directory -Force backup
docker exec hau_postgres pg_dump -U postgres -d DigitalSign_OCR -Fc -f /tmp/DigitalSign_OCR.dump
docker cp hau_postgres:/tmp/DigitalSign_OCR.dump .\backup\DigitalSign_OCR.dump
```

### 12.2. Backup file MinIO trên máy cũ

```powershell
$backupPath = (Resolve-Path .\backup).Path
docker run --rm --entrypoint /bin/sh --network container:hau_minio -v "${backupPath}:/backup" quay.io/minio/mc:latest -c "mc alias set local http://localhost:9000 minioadmin minioadmin >/dev/null && mc mirror local/documents /backup/minio-documents"
```

Nếu đã đổi MinIO credential trong `.env`, thay `minioadmin minioadmin` bằng `MINIO_ROOT_USER MINIO_ROOT_PASSWORD` thực tế.

### 12.3. Backup chứng thư số SignService trên máy cũ

Thư mục `/app/certs` trong container `hau_sign_service` chứa `rootca.pfx` và các PFX user. Cần backup phần này nếu muốn giữ khả năng ký/xác minh chứng thư đã cấp.

```powershell
docker cp hau_sign_service:/app/certs .\backup\sign-certs
```

Copy thư mục `backup/` sang máy mới.

### 12.4. Restore trên máy mới

Khởi động hạ tầng trước:

```powershell
docker compose up -d postgres minio kafka minio-init
```

Restore PostgreSQL:

```powershell
docker cp .\backup\DigitalSign_OCR.dump hau_postgres:/tmp/DigitalSign_OCR.dump
docker exec hau_postgres pg_restore -U postgres -d DigitalSign_OCR --clean --if-exists /tmp/DigitalSign_OCR.dump
```

Restore MinIO:

```powershell
$backupPath = (Resolve-Path .\backup).Path
docker run --rm --entrypoint /bin/sh --network container:hau_minio -v "${backupPath}:/backup" quay.io/minio/mc:latest -c "mc alias set local http://localhost:9000 minioadmin minioadmin >/dev/null && mc mb -p local/documents || true && mc mirror --overwrite /backup/minio-documents local/documents"
```

Restore chứng thư số SignService:

```powershell
docker compose up -d sign-service
docker cp .\backup\sign-certs\. hau_sign_service:/app/certs
docker compose restart sign-service
```

Sau đó chạy toàn bộ app:

```powershell
docker compose up -d
```

## 12.5. Checklist đổi secret khi triển khai thật

Các giá trị mặc định trong repo chỉ phù hợp môi trường dev/demo. Trước khi dùng thật, copy `.env.example` thành `.env` và đổi:

- PostgreSQL username/password.
- MinIO root user/password.
- JWT key trong IdentityService, Gateway, DocumentService, SignService.
- OCR service-token: `ServiceAuth__OcrServiceToken` của DocumentService và `SERVICE_TOKEN` của OCRService phải cùng giá trị.
- Gmail app password hoặc SMTP account cho forgot/reset password.
- `EmailSettings:Username` và `EmailSettings:Password` của IdentityService.
- Mật khẩu Root CA/PFX trong SignService nếu đưa vào môi trường thật.
- Cấu hình HTTPS/reverse proxy nếu public ra mạng ngoài.

Không commit secret thật vào repo. Nên dùng biến môi trường, Docker secrets hoặc secret manager của hạ tầng triển khai.

## 13. Trường hợp máy mới không có internet

Trên máy cũ, sau khi đã build/pull đủ image:

```powershell
docker save -o hau_docker_images.tar `
  hau/api-gateway:local `
  hau/identity-service:local `
  hau/document-service:local `
  hau/sign-service:local `
  hau/ocr-service:local `
  hau/frontend:local `
  postgres:16-alpine `
  quay.io/minio/minio:latest `
  quay.io/minio/mc:latest `
  apache/kafka:3.7.2
```

Copy `hau_docker_images.tar` và source sang máy mới, rồi load image:

```powershell
docker load -i hau_docker_images.tar
docker compose up -d --no-build
```

Nếu source có thay đổi code, máy mới vẫn cần build lại hoặc cần copy lại image mới đã build từ máy cũ.
