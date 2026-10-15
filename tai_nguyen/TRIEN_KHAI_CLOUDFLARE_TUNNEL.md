# Triển khai Cloudflare Tunnel

File `docker-compose.tunnel.yml` chạy `cloudflared` trên mạng Docker Compose để gọi Frontend bằng DNS service. Tunnel chỉ cần token riêng của tunnel; API token và R2 key không dùng để chạy connector.

## 1. Chuẩn bị Cloudflare

1. Xác nhận zone `hauquanlycongvan.com` đang **Active** trong Cloudflare.
2. Vào **Networking > Tunnels**, tạo tunnel loại **Cloudflared**, quản lý trong Dashboard.
3. Published application hiện dùng:
   - `hauquanlycongvan.com` → `http://frontend:3000`
   `cloudflared` và Frontend cùng mạng Compose; `frontend` là DNS service ổn định, còn Nginx lắng nghe cổng `3000`. Nginx phục vụ giao diện và chuyển `/api/**` đến API Gateway. Nếu có route `www.hauquanlycongvan.com`, đặt origin tương tự.
4. Sao chép **Tunnel token** từ lệnh Docker ở tab cài connector. Đây là chuỗi token của tunnel, không phải Cloudflare API token/R2 access key. Lưu token vào `.env.tunnel` trên máy triển khai; không commit vào Git.

Trình duyệt dùng cùng origin `https://hauquanlycongvan.com/`. Nginx chuyển `/api/**` đến Gateway; Gateway tiếp tục chuyển `/api/auth/**`, `/api/documents/**`, `/api/signatures/**`, `/api/ocr/**` đến service tương ứng. Các service và Gateway không cần hostname công khai riêng.

## 2. Chuẩn bị máy chạy Docker

Cài Docker Engine và Docker Compose plugin, chuyển repository sang máy chạy. Sao chép `.env.example` thành `.env`, thay tất cả giá trị mẫu dùng cho PostgreSQL, MinIO, JWT, OCR và email theo môi trường. Đặt token tunnel trong file `.env.tunnel`:

```dotenv
TUNNEL_TOKEN=<tunnel-token-tu-dashboard>
```

Đặt URL API công khai trong `.env`:

```dotenv
PUBLIC_API_BASE_URL=https://hauquanlycongvan.com/
```

Không đặt API token hoặc R2 key vào `TUNNEL_TOKEN`. `.env` và `.env.tunnel` đã được `.gitignore` loại khỏi Git. Nếu các khóa trong ảnh từng được dùng, thu hồi và tạo khóa mới trong Cloudflare.

Trong Cloudflare, đặt origin của Published application thành `http://frontend:3000`, rồi chạy toàn bộ Compose với overlay tunnel:

```bash
docker compose -f docker-compose.yml -f docker-compose.tunnel.yml up -d --build
```

Khi cập nhật bằng Compose, luôn dùng cả hai file cấu hình. Có thể chạy script để bật toàn bộ stack và tự tạo lại `cloudflared` sau khi Frontend đã sẵn sàng:

- Windows: `./scripts/Start-Stack.ps1` (thêm `-Build` nếu cần build image).
- Linux: `bash scripts/start-stack.sh` (thêm `--build` nếu cần build image).

Sau khi đăng nhập Windows, bật **Start Docker Desktop when you sign in** trong cài đặt Docker Desktop. Các container dùng `restart: unless-stopped`, nên Docker sẽ tự khởi chạy chúng khi Docker Engine khởi động lại. Script dùng khi cần khởi động thủ công hoặc cập nhật stack để bảo đảm tunnel bám vào đúng Frontend.

Nếu đã chạy riêng `docker compose up -d frontend`, khôi phục tunnel bằng:

```bash
docker compose -f docker-compose.yml -f docker-compose.tunnel.yml up -d --no-deps --force-recreate cloudflared
```

Kiểm tra container và connector:

```bash
docker compose -f docker-compose.yml -f docker-compose.tunnel.yml ps
docker compose -f docker-compose.yml -f docker-compose.tunnel.yml logs --tail=50 cloudflared
```

Kiểm tra `https://hauquanlycongvan.com` trong trình duyệt và `https://hauquanlycongvan.com/health`. Frontend Blazor WebAssembly nhận `PUBLIC_API_BASE_URL` lúc container Nginx khởi động; cùng một image có thể chạy ở máy khác với URL API khác mà không cần build lại. Tunnel truy cập Nginx qua `frontend:3000`; Nginx truy cập `api-gateway:8080` qua mạng Docker Compose.

Nếu đang chuyển từ cấu hình cũ, sau khi đổi origin của Published application trong Cloudflare Dashboard/API sang `http://frontend:3000`, tạo lại riêng connector để nó tham gia mạng Compose:

```bash
docker compose -f docker-compose.yml -f docker-compose.tunnel.yml up -d --no-deps --force-recreate cloudflared
```

Các port trên host trong Compose cơ sở chỉ bind `127.0.0.1` để có thể kiểm tra tại máy chủ mà không mở trực tiếp service ra Internet. Cloudflare Tunnel không cần mở cổng inbound trên VPS.

## Điều khiển tunnel trong Docker Desktop

Bạn có thể bật/tắt tunnel trực tiếp trong Docker Desktop:

1. Mở project `digitalsign_ocrproject` và tìm container/service `cloudflared`.
2. Bấm **Stop** ở đúng dòng `cloudflared` để tắt truy cập qua tên miền; các container ứng dụng vẫn chạy và truy cập local vẫn hoạt động.
3. Khi cần public lại, bấm **Start** ở dòng `cloudflared`.

Chỉ thao tác trên riêng `cloudflared`; không bấm Stop ở toàn bộ Compose project nếu muốn ứng dụng tiếp tục chạy. Cấu hình `restart: unless-stopped` giữ container đã dừng thủ công ở trạng thái tắt qua lần Docker khởi động lại. Nếu bạn recreate frontend khi tunnel đang tắt, hãy dùng lệnh bật bên dưới để đưa connector trở lại mạng Compose.

Có thể dùng các script sau thay cho nút trong Docker Desktop khi cần:

- Windows bật/tắt: `./scripts/Start-Tunnel.ps1`, `./scripts/Stop-Tunnel.ps1`.
- Linux bật/tắt: `bash scripts/start-tunnel.sh`, `bash scripts/stop-tunnel.sh`.
## 3. Chuyển sang máy khác

Chuyển source hoặc image, cấu hình `.env` và dữ liệu cần giữ sang máy mới. Các Docker named volumes chứa PostgreSQL, MinIO, Kafka và chứng thư ký số **không tự đi theo** source; cần backup/restore riêng trước khi đổi máy. Nếu dùng cùng token trên hai máy cùng lúc, cả hai là connector của một tunnel; hãy dừng connector trên máy cũ trước khi chuyển hẳn sang máy mới để tránh yêu cầu bị chuyển đến hai bản dữ liệu khác nhau.

VPS 1 GB RAM không phù hợp chạy nguyên stack Compose này, nhất là OCR/Kafka. Hãy dùng máy đủ tài nguyên để thử tunnel trước; việc tách OCR và thiết kế Compose cho VPS nhỏ là bước triển khai riêng.
