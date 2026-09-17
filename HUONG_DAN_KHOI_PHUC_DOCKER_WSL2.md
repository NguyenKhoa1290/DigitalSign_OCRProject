# Khôi phục Docker sau khi chuyển Hyper-V sang WSL2

Tài liệu này dùng khi Docker Desktop bị mất toàn bộ image/container sau khi đổi backend Hyper-V/WSL2 hoặc reset Docker data.

## 1. Kiểm tra Docker đang chạy

Chạy tại thư mục root project:

```powershell
docker --version
docker compose version
docker info --format "{{json .}}" | Select-String "WSL2"
docker images
```

Nếu `docker images` trống hoặc thiếu các image `hau/*`, cần build lại.

## 2. Build lại image

Build toàn bộ:

```powershell
docker compose build
```

Lần đầu sẽ lâu vì phải tải lại:

- .NET SDK/runtime image.
- Python/PaddleOCR dependency cho `ocr-service`.
- `wasm-tools` cho frontend.
- PostgreSQL, MinIO, Kafka khi chạy `docker compose up`.

Nếu build toàn bộ bị kẹt/lỗi mạng ở OCR, có thể build lại riêng OCR sau khi các image khác đã xong:

```powershell
docker compose build ocr-service
```

Kiểm tra image custom đã có:

```powershell
docker images --format "table {{.Repository}}\t{{.Tag}}\t{{.Size}}"
```

Cần thấy tối thiểu:

```text
hau/api-gateway        local
hau/identity-service   local
hau/document-service   local
hau/sign-service       local
hau/ocr-service        local
hau/frontend           local
```

## 3. Chạy lại stack

```powershell
docker compose up -d
docker compose ps
```

Trạng thái mong muốn:

- `hau_postgres`: `healthy`.
- `hau_identity_service`: `healthy`.
- Các container còn lại: `Up`.
- `hau_minio_init`: có thể `Exited (0)` nếu xem bằng `docker ps -a`; đây là bình thường.

## 4. Kiểm tra sau khi chạy

Health/API:

```powershell
Invoke-WebRequest http://localhost:5000/health -UseBasicParsing
Invoke-WebRequest http://localhost:5000/ -UseBasicParsing
Invoke-WebRequest http://localhost:5048/health -UseBasicParsing
Invoke-WebRequest http://localhost:5051/api/ocr/health -UseBasicParsing
Invoke-WebRequest http://localhost:5227/ -UseBasicParsing
```

Login:

```powershell
$payload = @{ username = "admin"; password = "Admin@123" } | ConvertTo-Json
Invoke-RestMethod `
  -Uri "http://localhost:5000/api/auth/login" `
  -Method Post `
  -Body $payload `
  -ContentType "application/json"
```

MinIO bucket:

```powershell
docker run --rm `
  --network digitalsign_ocrproject_default `
  --entrypoint /bin/sh quay.io/minio/mc:latest `
  -c "mc alias set local http://minio:9000 minioadmin minioadmin >/dev/null && mc ls local"
```

Kết quả cần có bucket:

```text
documents/
```

Kafka topic:

```powershell
docker compose exec -T kafka `
  /opt/kafka/bin/kafka-topics.sh `
  --bootstrap-server localhost:9092 `
  --list
```

Kết quả cần có:

```text
document.uploaded
```

## 5. Kết quả kiểm tra ngày 17/09/2026

Sau khi chuyển sang Docker Desktop WSL2:

- Docker engine chạy trên WSL2.
- Docker ban đầu có `Images: 0`.
- Đã build lại các image:
  - `hau/api-gateway:local`
  - `hau/identity-service:local`
  - `hau/document-service:local`
  - `hau/sign-service:local`
  - `hau/ocr-service:local`
  - `hau/frontend:local`
- `docker compose up -d`: chạy thành công.
- Health check:
  - Gateway `/health`: HTTP 200.
  - Gateway `/`: HTTP 200.
  - Identity `/health`: HTTP 200.
  - OCR `/api/ocr/health`: HTTP 200.
  - Frontend `/`: HTTP 200.
- Login `admin / Admin@123`: thành công, có access token, role `Admin`.
- MinIO có bucket `documents`.
- Kafka có topic `document.uploaded`.

## 6. Lưu ý dữ liệu

Nếu Docker Desktop đổi backend hoặc reset data thì không chỉ image mà cả volume cũng có thể mất. Khi đó database, file MinIO và certificate SignService sẽ là môi trường mới rỗng.

Các volume quan trọng:

```text
digitalsign_ocrproject_hau_postgres_data
digitalsign_ocrproject_hau_minio_data
digitalsign_ocrproject_hau_kafka_data
digitalsign_ocrproject_hau_sign_certs
```

Nếu cần giữ dữ liệu thật, backup theo mục “Mang cả dữ liệu sang máy khác” trong `TRIEN_KHAI_DOCKER.md`.

## 7. Lưu image để dùng offline/lần sau

Sau khi build thành công, có thể export image:

```powershell
New-Item -ItemType Directory -Force backup
docker save -o .\backup\hau_docker_images.tar `
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

Khôi phục image:

```powershell
docker load -i .\backup\hau_docker_images.tar
docker compose up -d --no-build
```

