# Hướng dẫn chạy AI local bằng Ollama

FinViet sử dụng Ollama qua API tương thích OpenAI. Mỗi thành viên có thể chạy AI trực tiếp trên máy mà không cần Gemini API key.

## 1. Cấu hình khuyến nghị

| Mục đích | Model | Dung lượng |
|---|---|---:|
| Phân loại, chat, báo cáo | `qwen3:4b` | Khoảng 2,5 GB |
| Embedding cho RAG | `nomic-embed-text` | Khoảng 274 MB |

`nomic-embed-text` tạo vector 768 chiều, phù hợp với cột PostgreSQL `vector(768)` của dự án.

Khuyến nghị tối thiểu:

- RAM: 16 GB.
- GPU: 4–6 GB VRAM hoặc cao hơn.
- Nếu không có GPU, Ollama vẫn có thể chạy bằng CPU nhưng phản hồi sẽ chậm hơn.

## 2. Cài đặt Ollama

Tải bản Windows tại:

<https://ollama.com/download/windows>

Sau khi cài, mở terminal mới và kiểm tra:

```powershell
ollama --version
```

Nếu Ollama server chưa chạy:

```powershell
ollama serve
```

Kiểm tra API:

```powershell
Invoke-RestMethod http://localhost:11434/api/tags
```

Ollama local mặc định chạy tại `http://localhost:11434` và **không yêu cầu API key**.

## 3. Tải model

```powershell
ollama pull qwen3:4b
ollama pull nomic-embed-text
ollama list
```

Danh sách model cần có:

```text
qwen3:4b
nomic-embed-text:latest
```

## 4. Kiểm tra model

### Kiểm tra chat

```powershell
$body = @{
    model = "qwen3:4b"
    messages = @(
        @{
            role = "user"
            content = "Trả lời chính xác một từ: OK"
        }
    )
    stream = $false
    temperature = 0
} | ConvertTo-Json -Depth 6

$response = Invoke-RestMethod `
    -Method Post `
    -Uri "http://localhost:11434/v1/chat/completions" `
    -ContentType "application/json" `
    -Body $body `
    -TimeoutSec 180

$response.choices[0].message.content
```

Kết quả mong đợi:

```text
OK
```

### Kiểm tra embedding

```powershell
$body = @{
    model = "nomic-embed-text"
    input = "Kiểm tra embedding FinViet"
} | ConvertTo-Json

$response = Invoke-RestMethod `
    -Method Post `
    -Uri "http://localhost:11434/v1/embeddings" `
    -ContentType "application/json" `
    -Body $body `
    -TimeoutSec 180

$response.data[0].embedding.Count
```

Kết quả bắt buộc là:

```text
768
```

## 5. Cấu hình backend

Cấu hình section `Ai`:

```json
{
  "Ai": {
    "BaseUrl": "http://localhost:11434/v1",
    "ApiKey": "",
    "ClassificationModel": "qwen3:4b",
    "GenerationModel": "qwen3:4b",
    "EmbeddingModel": "nomic-embed-text",
    "EmbeddingDimensions": 768,
    "TimeoutSeconds": 180,
    "RagEnabled": false
  }
}
```

Ý nghĩa:

| Thuộc tính | Mô tả |
|---|---|
| `BaseUrl` | API tương thích OpenAI của Ollama |
| `ApiKey` | Để trống khi chạy Ollama local |
| `ClassificationModel` | Model phân loại giao dịch |
| `GenerationModel` | Model chat, nhận xét và báo cáo |
| `EmbeddingModel` | Model tạo vector RAG |
| `EmbeddingDimensions` | Phải là 768 để khớp database |
| `TimeoutSeconds` | Timeout dài hơn vì model local có thể khởi động chậm |
| `RagEnabled` | Chỉ bật sau khi dữ liệu RAG đã dùng cùng embedding model |

Không commit API key hoặc secret vào Git. Nếu sau này dùng một provider có xác thực, cấu hình key bằng .NET User Secrets:

```powershell
dotnet user-secrets set "Ai:ApiKey" "<api-key>" --project src/FinViet.Api
```

## 6. Chạy FinViet API

Đảm bảo PostgreSQL và Ollama đang chạy, sau đó:

```powershell
dotnet restore FinViet.sln
dotnet run --project src/FinViet.Api
```

Địa chỉ Development mặc định:

- API: `http://localhost:5122`
- Swagger: `http://localhost:5122/swagger`

Model không được nạp vào GPU ngay khi chạy backend. Ollama chỉ nạp model khi có request AI đầu tiên. Vì vậy VRAM ở mức 0 GB trước khi gọi AI là bình thường.

Kiểm tra model và GPU đang sử dụng:

```powershell
ollama ps
nvidia-smi
```

## 7. Kiểm tra chức năng AI

Trên Swagger:

1. Gọi `POST /api/auth/login` để lấy `accessToken`.
2. Nhấn **Authorize** và nhập token.
3. Gọi `POST /api/ai/categorize/preview` với body:

```json
{
  "input": "Grab 4.7km"
}
```

Kết quả mong đợi tương tự:

```json
{
  "success": true,
  "data": {
    "categoryName": "Di chuyển",
    "confidence": 1.0
  }
}
```

Có thể tiếp tục kiểm tra chat, điểm chi tiêu và báo cáo tuần qua các endpoint `/api/ai/*`.

## 8. Chuyển dữ liệu RAG cũ

Nếu database đã có embedding được tạo bằng Gemini, phải re-index trước khi bật RAG. Vector Gemini và `nomic-embed-text` không tương thích về ngữ nghĩa dù đều có 768 chiều.

> Không xóa `rag_document` hoặc `rag_chunk`. Quy trình dưới đây cập nhật embedding tại chỗ và giữ nguyên tài liệu, nội dung và quyền sở hữu.

### Bước 1: Giữ RAG ở trạng thái tắt

```json
"RagEnabled": false
```

Chat vẫn hoạt động bằng dữ liệu tài chính tổng hợp, nhưng chưa truy xuất vector cũ.

### Bước 2: Kiểm tra số lượng dữ liệu

```powershell
psql -h localhost -U admin -d Finviet_update -c "SELECT COUNT(*) AS documents FROM rag_document; SELECT COUNT(*) AS chunks FROM rag_chunk;"
```

Điều chỉnh host, username và database theo `ConnectionStrings:DefaultConnection` của máy.

### Bước 3: Sao lưu PostgreSQL

```powershell
pg_dump `
    -h localhost `
    -U admin `
    -d Finviet_update `
    -Fc `
    -f "D:\backup\finviet-before-ollama.dump"
```

Kiểm tra file backup đã tồn tại và có dung lượng hợp lý trước khi tiếp tục.

### Bước 4: Chạy re-index

Đảm bảo Ollama và `nomic-embed-text` đang hoạt động, sau đó chạy:

```powershell
dotnet run --project src/FinViet.Api -- --reindex-rag --confirm-reindex
```

Lệnh sẽ:

- Từ chối chạy nếu thiếu `--confirm-reindex`.
- Từ chối chạy nếu `RagEnabled=true`.
- Tạo lại embedding theo từng batch.
- Giữ nguyên document và chunk.
- Kiểm tra corpus không thay đổi trong khi xử lý.
- Xây dựng lại index pgvector sau khi hoàn tất.
- Thoát mà không khởi động HTTP server.

Nếu quá trình bị lỗi hoặc gián đoạn, giữ `RagEnabled=false`, xử lý nguyên nhân rồi chạy lại lệnh.

### Bước 5: Bật lại RAG

Sau khi re-index thành công:

1. Kiểm tra lại số lượng document/chunk.
2. Chạy API và thử một số câu hỏi chat có sử dụng tài liệu.
3. Đổi cấu hình thành:

```json
"RagEnabled": true
```

4. Khởi động lại API.

## 9. Lỗi thường gặp

### Không kết nối được `localhost:11434`

```powershell
ollama serve
Invoke-RestMethod http://localhost:11434/api/tags
```

### Báo không tìm thấy model

```powershell
ollama pull qwen3:4b
ollama pull nomic-embed-text
ollama list
```

Tên model trong cấu hình phải trùng với kết quả `ollama list`.

### Request đầu tiên chậm

Đây là bình thường vì Ollama cần nạp model vào RAM/VRAM. Các request tiếp theo thường nhanh hơn khi model còn được giữ trong bộ nhớ.

### Hết VRAM hoặc chạy quá chậm

- Đóng ứng dụng đang dùng GPU.
- Giữ `qwen3:4b` và context mặc định 4096.
- Kiểm tra bằng `ollama ps` và `nvidia-smi`.
- Không đổi sang model 8B trên GPU 6 GB nếu chưa kiểm tra tài nguyên.

### API chạy trong Docker

Từ container, `localhost` trỏ đến chính container. Khi Ollama chạy trên Windows host, dùng:

```json
"BaseUrl": "http://host.docker.internal:11434/v1"
```

Không expose cổng Ollama trực tiếp ra Internet nếu chưa có reverse proxy và authentication.
