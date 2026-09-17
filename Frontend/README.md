# Frontend

Frontend là Blazor WebAssembly app cho hệ thống HAU DigitalSign OCR.

## Chạy local

Đảm bảo API Gateway đang chạy ở:

```text
http://localhost:5000
```

Chạy frontend:

```bash
dotnet run --project Frontend/HauDocumentApp.csproj
```

Port dev:

```text
http://localhost:5227
```

## Chạy bằng Docker

Frontend được publish thành static files và serve bằng Nginx:

```bash
docker compose up -d frontend
```

Hoặc chạy toàn bộ hệ thống:

```bash
docker compose up -d
```

URL:

```text
http://localhost:5227
```

Dockerfile của frontend đã cài `python3` và `dotnet workload install wasm-tools` trong build stage để `dotnet publish` chạy được native WebAssembly optimization.

Nếu publish frontend trực tiếp trên máy host, nên cài workload tương ứng:

```bash
dotnet workload install wasm-tools
```

## Cấu hình API Gateway

`Frontend/Program.cs` hiện cấu hình:

```csharp
BaseAddress = new Uri("http://localhost:5000/")
```

Nếu người dùng mở frontend từ máy khác trong LAN, cần đổi `localhost` thành IP/domain của máy deploy trước khi build frontend, ví dụ:

```csharp
BaseAddress = new Uri("http://192.168.1.50:5000/")
```

Sau đó build lại:

```bash
docker compose build frontend
docker compose up -d frontend
```

## Ghi chú hiện tại

- JWT được lưu trong localStorage.
- Frontend đã có các màn hình auth, dashboard, admin, documents, signatures.
- Chưa có màn hình OCR riêng.
- DTO ký số frontend đã đồng bộ với backend SignService: gửi `DocId`, `SignerId`, `SignerName`, `Reason` và map thao tác UI sang endpoint ký phù hợp.
