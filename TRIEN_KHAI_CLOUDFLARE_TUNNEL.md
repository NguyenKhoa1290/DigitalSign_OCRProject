# Triển khai Cloudflare Tunnel

File `docker-compose.tunnel.yml` chạy `cloudflared` cùng mạng Docker với Frontend và API Gateway. Tunnel chỉ cần token riêng của tunnel; API token và R2 key không dùng để chạy connector.

## 1. Chuẩn bị Cloudflare

1. Xác nhận zone `hauquanlycongvan.com` đang **Active** trong Cloudflare.
2. Vào **Networking > Tunnels**, tạo tunnel loại **Cloudflared**, quản lý trong Dashboard.
3. Published application hiện dùng:
   - `hauquanlycongvan.com` → `http://localhost:3000`
   `cloudflared` dùng chung network namespace với Frontend, nơi Nginx lắng nghe cổng `3000`. Nginx phục vụ giao diện và chuyển `/api/**` đến API Gateway. Có thể thêm `www.hauquanlycongvan.com` cùng route nếu cần bí danh.
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

Chạy toàn bộ Compose với overlay tunnel:

```bash
docker compose -f docker-compose.yml -f docker-compose.tunnel.yml up -d --build
```

Kiểm tra container và connector:

```bash
docker compose -f docker-compose.yml -f docker-compose.tunnel.yml ps
docker compose -f docker-compose.yml -f docker-compose.tunnel.yml logs --tail=50 cloudflared
```

Kiểm tra `https://hauquanlycongvan.com` trong trình duyệt và `https://hauquanlycongvan.com/health`. Frontend Blazor WebAssembly nhận `PUBLIC_API_BASE_URL` lúc container Nginx khởi động; cùng một image có thể chạy ở máy khác với URL API khác mà không cần build lại. Tunnel truy cập Nginx qua `localhost:3000`; Nginx truy cập `api-gateway:8080` qua mạng Docker Compose.

Các port trên host trong Compose cơ sở chỉ bind `127.0.0.1` để có thể kiểm tra tại máy chủ mà không mở trực tiếp service ra Internet. Cloudflare Tunnel không cần mở cổng inbound trên VPS.

## 3. Chuyển sang máy khác

Chuyển source hoặc image, cấu hình `.env` và dữ liệu cần giữ sang máy mới. Các Docker named volumes chứa PostgreSQL, MinIO, Kafka và chứng thư ký số **không tự đi theo** source; cần backup/restore riêng trước khi đổi máy. Nếu dùng cùng token trên hai máy cùng lúc, cả hai là connector của một tunnel; hãy dừng connector trên máy cũ trước khi chuyển hẳn sang máy mới để tránh yêu cầu bị chuyển đến hai bản dữ liệu khác nhau.

VPS 1 GB RAM không phù hợp chạy nguyên stack Compose này, nhất là OCR/Kafka. Hãy dùng máy đủ tài nguyên để thử tunnel trước; việc tách OCR và thiết kế Compose cho VPS nhỏ là bước triển khai riêng.
